using System.Text.RegularExpressions;
using AdaptAula.Domain;

namespace AdaptAula.Validation;

/// <summary>
/// Deterministic VALIDATE step (spec §7, §11; V2 §8, §17). Runs after the AI has produced
/// <see cref="AdaptedQuestion"/> drafts — and again on the teacher's edited document before export — and compares them
/// against the originals plus the resolved <see cref="AdaptationPlan"/>. Never decides silently: every ERROR blocks
/// export, every WARNING/REVIEW is surfaced to the teacher.
/// </summary>
public static class SafetyValidator
{
    private static readonly Regex LeadingNumber = new(@"^\s*(\d{1,3})\s*[\.\)\-]", RegexOptions.Compiled);

    public static List<ValidationResult> Validate(
        AdaptationPlan plan,
        Assessment assessment,
        IReadOnlyDictionary<Guid, Question> questionsById,
        IReadOnlyList<AdaptedQuestion> adaptedQuestions,
        double? extractionConfidence = null)
    {
        var results = new List<ValidationResult>();

        ValidatePoints(plan, assessment, questionsById, adaptedQuestions, results);
        ValidateQuestionCount(plan, questionsById, adaptedQuestions, results);

        foreach (var adapted in adaptedQuestions)
        {
            if (!questionsById.TryGetValue(adapted.QuestionId, out var original))
                continue;

            ValidateContentCoverage(plan, adapted, original, results);
            ValidateConstructBypass(plan, adapted, original, results);
            ValidateHintRevealsAnswer(plan, adapted, original, results);
            ValidateExcessiveHints(plan, adapted, original, results);
            ValidateDifficultyReduced(plan, adapted, original, results);
            ValidateProtectedVocabulary(plan, assessment, adapted, original, results);
            ValidateLanguage(plan, assessment, adapted, original, results);
            ValidateNumbering(plan, adapted, original, results);
            ValidatePendingProposal(plan, adapted, original, results);
        }

        ValidateCriteriaCoverage(plan, questionsById, adaptedQuestions, results);
        ValidateCurricularChange(plan, results);
        ValidateExtractionConfidence(plan, extractionConfidence, results);
        ValidateUnsupportedConflicts(plan, results);

        return results;
    }

    /// <summary>Whether the plan can be exported given a set of validation results:
    /// blocked while any ERROR is unresolved (spec §11/§12 hard rules table).</summary>
    public static bool CanExport(IEnumerable<ValidationResult> results) =>
        results.All(r => r.Severity != ValidationSeverity.Error);

    private static bool IsLocked(AdaptationPlan plan, string element) => plan.Locks.Contains(element);

    private static ValidationResult Result(
        AdaptationPlan plan, ValidationSeverity severity, ValidationCode code, string message,
        Guid? questionId = null, string? original = null, string? adapted = null, bool? requiresReview = null) => new()
    {
        PlanId = plan.Id,
        Severity = severity,
        Code = code,
        QuestionId = questionId,
        Message = message,
        OriginalValue = original,
        AdaptedValue = adapted,
        RequiresReview = requiresReview ?? severity != ValidationSeverity.Warning
    };

    private static void ValidatePoints(
        AdaptationPlan plan, Assessment assessment, IReadOnlyDictionary<Guid, Question> questionsById,
        IReadOnlyList<AdaptedQuestion> adaptedQuestions, List<ValidationResult> results)
    {
        // Structurally guarded upstream (AdaptedQuestion.Points is always copied from the original
        // in the pipeline, never returned by the AI) — verified here as a hard, independent check.
        var totalAdapted = adaptedQuestions.Sum(a => a.Points);
        if (totalAdapted != assessment.TotalPoints)
        {
            results.Add(Result(plan, ValidationSeverity.Error, ValidationCode.PointsChanged,
                $"La puntuación total adaptada ({totalAdapted}) no coincide con el original ({assessment.TotalPoints}).",
                original: assessment.TotalPoints.ToString(), adapted: totalAdapted.ToString()));
        }

        foreach (var adapted in adaptedQuestions)
        {
            if (!questionsById.TryGetValue(adapted.QuestionId, out var original)) continue;
            if (adapted.Points != original.Points)
            {
                results.Add(Result(plan, ValidationSeverity.Error, ValidationCode.PointsChanged,
                    "Los puntos de la pregunta cambiaron respecto al original.", original.Id,
                    original.Points.ToString(), adapted.Points.ToString()));
            }
        }
    }

    /// <summary>Every original question must still exist exactly once (V2 §8 "número de preguntas").</summary>
    private static void ValidateQuestionCount(
        AdaptationPlan plan, IReadOnlyDictionary<Guid, Question> questionsById,
        IReadOnlyList<AdaptedQuestion> adaptedQuestions, List<ValidationResult> results)
    {
        var present = adaptedQuestions.Select(a => a.QuestionId).ToHashSet();
        var severity = IsLocked(plan, ProtectedElements.QuestionCount) ? ValidationSeverity.Error : ValidationSeverity.Warning;

        foreach (var original in questionsById.Values.Where(q => !present.Contains(q.Id)).OrderBy(q => q.Order))
        {
            results.Add(Result(plan, severity, ValidationCode.QuestionMissing,
                $"La pregunta {original.Order + 1} ya no está en la versión adaptada.", original.Id));
        }

        if (adaptedQuestions.Count != questionsById.Count)
        {
            results.Add(Result(plan, severity, ValidationCode.QuestionCountChanged,
                $"El número de preguntas cambió: original {questionsById.Count}, adaptada {adaptedQuestions.Count}.",
                original: questionsById.Count.ToString(), adapted: adaptedQuestions.Count.ToString()));
        }
    }

    private static void ValidateContentCoverage(
        AdaptationPlan plan, AdaptedQuestion adapted, Question original, List<ValidationResult> results)
    {
        // With the teacher's "contenido evaluado" lock every question is held to a stricter bar; otherwise only
        // questions the teacher tagged with a construct are (the original behaviour).
        var locked = IsLocked(plan, ProtectedElements.EvaluatedContent) || IsLocked(plan, ProtectedElements.Criteria);
        if (original.ConstructTags.Count == 0 && !locked) return;

        var threshold = locked ? 0.5 : 0.3;
        var coverage = TextHeuristics.KeywordCoverage(original.OriginalText, adapted.AdaptedText);
        if (coverage < threshold)
        {
            results.Add(Result(plan, ValidationSeverity.Error, ValidationCode.ContentDropped,
                $"Solo el {coverage:P0} de las palabras clave del original aparece en la versión adaptada; puede haberse perdido contenido necesario.",
                original.Id));
        }
    }

    private static void ValidateConstructBypass(
        AdaptationPlan plan, AdaptedQuestion adapted, Question original, List<ValidationResult> results)
    {
        if (!plan.ResolvedRulesByQuestion.TryGetValue(original.Id, out var resolvedRules)) return;

        var appliedRuleIds = resolvedRules.Where(r => r.Applied).Select(r => r.RuleId).ToHashSet();

        if (original.ConstructTags.Contains(ConstructTags.Reading) &&
            appliedRuleIds.Contains("dyslexia.read_aloud_audio"))
        {
            results.Add(Result(plan, ValidationSeverity.Error, ValidationCode.ReadingConstructBypassed,
                "Se aplicó lectura en voz alta/audio en una pregunta cuyo constructo es la lectura.", original.Id));
        }

        if (original.ConstructTags.Contains(ConstructTags.Spelling) &&
            appliedRuleIds.Contains("dysorthography.word_bank_corrector"))
        {
            results.Add(Result(plan, ValidationSeverity.Error, ValidationCode.SpellingConstructBypassed,
                "Se activó un banco de palabras/corrector en una pregunta cuyo constructo es la ortografía.", original.Id));
        }
    }

    private static void ValidateHintRevealsAnswer(
        AdaptationPlan plan, AdaptedQuestion adapted, Question original, List<ValidationResult> results)
    {
        var hintText = string.Join(" ", adapted.Supports.Append(adapted.AdaptedText));
        if (TextHeuristics.ContainsAnswer(hintText, original.ExpectedAnswer))
        {
            results.Add(Result(plan, ValidationSeverity.Error, ValidationCode.HintRevealsAnswer,
                "Un ejemplo, apoyo o glosario generado contiene la respuesta esperada.", original.Id));
            return;
        }

        // A reworded or split answer slips past the exact-match check above.
        if (TextHeuristics.AnswerLeakRatio(hintText, original.OriginalText, original.ExpectedAnswer) >= 0.6)
        {
            results.Add(Result(plan, ValidationSeverity.Review, ValidationCode.HintRevealsAnswer,
                "Un apoyo o ejemplo contiene casi todas las palabras de la respuesta esperada; revisa que no la regale.", original.Id));
        }
    }

    /// <summary>"Posibles pistas excesivas" (V2 §17): too many supports, or supports far longer than the question itself.</summary>
    private static void ValidateExcessiveHints(
        AdaptationPlan plan, AdaptedQuestion adapted, Question original, List<ValidationResult> results)
    {
        var supportWords = adapted.Supports.Sum(TextHeuristics.WordCount);
        var originalWords = Math.Max(1, TextHeuristics.WordCount(original.OriginalText));

        if (adapted.Supports.Count > 4 || (supportWords > 25 && supportWords > originalWords * 2))
        {
            results.Add(Result(plan, ValidationSeverity.Warning, ValidationCode.ExcessiveHints,
                $"Esta pregunta lleva {adapted.Supports.Count} apoyos ({supportWords} palabras) frente a {originalWords} del enunciado: pueden ser demasiadas pistas.",
                original.Id));
        }
    }

    private static void ValidateDifficultyReduced(
        AdaptationPlan plan, AdaptedQuestion adapted, Question original, List<ValidationResult> results)
    {
        var originalWords = TextHeuristics.WordCount(original.OriginalText);
        var adaptedWords = TextHeuristics.WordCount(adapted.AdaptedText);
        if (originalWords >= 8 && adaptedWords < originalWords * 0.4)
        {
            // With «nivel/demanda cognitiva» locked, a much shorter statement is treated as a probable drop in demand.
            var locked = IsLocked(plan, ProtectedElements.CognitiveDemand);
            results.Add(Result(plan,
                locked ? ValidationSeverity.Error : ValidationSeverity.Warning,
                locked ? ValidationCode.CognitiveDemandChanged : ValidationCode.DifficultyReduced,
                locked
                    ? "El texto adaptado es mucho más corto y la demanda cognitiva está protegida: puede haberse rebajado la exigencia."
                    : "El texto adaptado es mucho más corto que el original; revisa que no se haya reducido la exigencia.",
                original.Id, requiresReview: locked));
        }
    }

    private static void ValidateProtectedVocabulary(
        AdaptationPlan plan, Assessment assessment, AdaptedQuestion adapted, Question original, List<ValidationResult> results)
    {
        var haystack = string.Join(" ", adapted.Supports.Prepend(adapted.AdaptedText));
        foreach (var term in assessment.ProtectedVocabulary.Where(t => !string.IsNullOrWhiteSpace(t)))
        {
            if (!TextHeuristics.ContainsTerm(original.OriginalText, term)) continue;
            if (TextHeuristics.ContainsTerm(adapted.AdaptedText, term)) continue;

            results.Add(Result(plan, ValidationSeverity.Error, ValidationCode.ProtectedVocabularyMissing,
                $"El término protegido «{term.Trim()}» estaba en el original y ya no aparece en el enunciado adaptado.",
                original.Id, original: term.Trim(),
                adapted: TextHeuristics.ContainsTerm(haystack, term) ? "solo en los apoyos" : "ausente"));
        }
    }

    private static void ValidateLanguage(
        AdaptationPlan plan, Assessment assessment, AdaptedQuestion adapted, Question original, List<ValidationResult> results)
    {
        var originalLanguage = TextHeuristics.DetectLanguage(original.OriginalText);
        var adaptedLanguage = TextHeuristics.DetectLanguage(adapted.AdaptedText);
        if (originalLanguage is null || adaptedLanguage is null || originalLanguage == adaptedLanguage) return;

        var translationAuthorized = plan.ResolvedRulesByQuestion.TryGetValue(original.Id, out var rules) &&
                                    rules.Any(r => r.Applied && r.RuleId == "spanish_l2.translation_only_if_authorized");
        var severity = IsLocked(plan, ProtectedElements.Language) && !translationAuthorized
            ? ValidationSeverity.Error
            : ValidationSeverity.Review;

        results.Add(Result(plan, severity, ValidationCode.LanguageChanged,
            $"El idioma del enunciado cambió ({originalLanguage} → {adaptedLanguage}).",
            original.Id, originalLanguage, adaptedLanguage));
    }

    /// <summary>The exporter adds its own "Pregunta N" heading, so only a number the AI wrote into the text itself can clash.</summary>
    private static void ValidateNumbering(
        AdaptationPlan plan, AdaptedQuestion adapted, Question original, List<ValidationResult> results)
    {
        var originalMatch = LeadingNumber.Match(original.OriginalText);
        var adaptedMatch = LeadingNumber.Match(adapted.AdaptedText);
        if (!originalMatch.Success || !adaptedMatch.Success) return;

        if (originalMatch.Groups[1].Value != adaptedMatch.Groups[1].Value)
        {
            results.Add(Result(plan, ValidationSeverity.Warning, ValidationCode.NumberingChanged,
                $"La numeración cambió: el original empieza por {originalMatch.Groups[1].Value} y la adaptación por {adaptedMatch.Groups[1].Value}.",
                original.Id, originalMatch.Groups[1].Value, adaptedMatch.Groups[1].Value));
        }
    }

    private static void ValidatePendingProposal(
        AdaptationPlan plan, AdaptedQuestion adapted, Question original, List<ValidationResult> results)
    {
        if (adapted.Proposal is not { Status: ProposalStatus.Pending }) return;

        results.Add(Result(plan, ValidationSeverity.Review, ValidationCode.ProposalPending,
            $"La IA propone un cambio que podría alterar lo evaluado y no se ha aplicado: {adapted.Proposal.Reason}",
            original.Id));
    }

    /// <summary>Criteria the teacher linked to questions must still be assessed by some question (V2 §17 "cobertura de criterios").</summary>
    private static void ValidateCriteriaCoverage(
        AdaptationPlan plan, IReadOnlyDictionary<Guid, Question> questionsById,
        IReadOnlyList<AdaptedQuestion> adaptedQuestions, List<ValidationResult> results)
    {
        var present = adaptedQuestions.Select(a => a.QuestionId).ToHashSet();
        var originalCriteria = questionsById.Values
            .Where(q => q.Analysis is not null)
            .SelectMany(q => q.Analysis!.CriteriaIds).ToHashSet();
        var keptCriteria = questionsById.Values
            .Where(q => present.Contains(q.Id) && q.Analysis is not null)
            .SelectMany(q => q.Analysis!.CriteriaIds).ToHashSet();

        foreach (var lost in originalCriteria.Except(keptCriteria).OrderBy(c => c, StringComparer.Ordinal))
        {
            results.Add(Result(plan, ValidationSeverity.Error, ValidationCode.CriteriaCoverage,
                $"El criterio de evaluación {lost} ya no lo evalúa ninguna pregunta de la versión adaptada.",
                original: lost, adapted: "sin cobertura"));
        }

        if (plan.IsCurricularChange && plan.CurricularCriteriaIds.Count > 0)
        {
            var notAssessed = plan.CurricularCriteriaIds.Where(c => !keptCriteria.Contains(c)).ToList();
            if (notAssessed.Count > 0 && originalCriteria.Count > 0)
            {
                results.Add(Result(plan, ValidationSeverity.Review, ValidationCode.CriteriaCoverage,
                    $"Criterios de referencia elegidos que ninguna pregunta evalúa todavía: {string.Join(", ", notAssessed)}."));
            }
        }
    }

    private static void ValidateCurricularChange(AdaptationPlan plan, List<ValidationResult> results)
    {
        if (!plan.IsCurricularChange) return;

        results.Add(Result(plan, ValidationSeverity.Review, ValidationCode.CurricularChange,
            "Cambio curricular activado explícitamente por el docente. Requiere aprobación obligatoria antes de exportar."));
    }

    private static void ValidateExtractionConfidence(
        AdaptationPlan plan, double? extractionConfidence, List<ValidationResult> results)
    {
        if (extractionConfidence.HasValue && extractionConfidence.Value < 0.6)
        {
            results.Add(Result(plan, ValidationSeverity.Review, ValidationCode.LowExtractionConfidence,
                $"La extracción del documento original tiene confianza baja ({extractionConfidence:P0}). Compara con el archivo original antes de aprobar."));
        }
    }

    private static void ValidateUnsupportedConflicts(AdaptationPlan plan, List<ValidationResult> results)
    {
        foreach (var warning in plan.Warnings.Where(w => w.StartsWith("UNSUPPORTED_CONFLICT")))
            results.Add(Result(plan, ValidationSeverity.Review, ValidationCode.UnsupportedConflict, warning));
    }
}
