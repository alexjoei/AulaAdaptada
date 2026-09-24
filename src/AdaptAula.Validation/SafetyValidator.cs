using AdaptAula.Domain;

namespace AdaptAula.Validation;

/// <summary>
/// Deterministic VALIDATE step (spec §7, §11). Runs after the AI has produced
/// <see cref="AdaptedQuestion"/> drafts and compares them against the originals plus the
/// resolved <see cref="AdaptationPlan"/>. Never decides silently: every ERROR blocks export,
/// every WARNING/REVIEW is surfaced to the teacher.
/// </summary>
public static class SafetyValidator
{
    public static List<ValidationResult> Validate(
        AdaptationPlan plan,
        Assessment assessment,
        IReadOnlyDictionary<Guid, Question> questionsById,
        IReadOnlyList<AdaptedQuestion> adaptedQuestions,
        double? extractionConfidence = null)
    {
        var results = new List<ValidationResult>();

        ValidatePoints(plan, assessment, questionsById, adaptedQuestions, results);

        foreach (var adapted in adaptedQuestions)
        {
            if (!questionsById.TryGetValue(adapted.QuestionId, out var original))
                continue;

            ValidateContentCoverage(plan, adapted, original, results);
            ValidateConstructBypass(plan, adapted, original, results);
            ValidateHintRevealsAnswer(plan, adapted, original, results);
            ValidateDifficultyReduced(plan, adapted, original, results);
        }

        ValidateCurricularChange(plan, results);
        ValidateExtractionConfidence(plan, extractionConfidence, results);
        ValidateUnsupportedConflicts(plan, results);

        return results;
    }

    /// <summary>Whether the plan can be exported given a set of validation results:
    /// blocked while any ERROR is unresolved (spec §11/§12 hard rules table).</summary>
    public static bool CanExport(IEnumerable<ValidationResult> results) =>
        results.All(r => r.Severity != ValidationSeverity.Error);

    private static void ValidatePoints(
        AdaptationPlan plan, Assessment assessment, IReadOnlyDictionary<Guid, Question> questionsById,
        IReadOnlyList<AdaptedQuestion> adaptedQuestions, List<ValidationResult> results)
    {
        // Structurally guarded upstream (AdaptedQuestion.Points is always copied from the original
        // in the pipeline, never returned by the AI) — verified here as a hard, independent check.
        var totalAdapted = adaptedQuestions.Sum(a => a.Points);
        if (totalAdapted != assessment.TotalPoints)
        {
            results.Add(new ValidationResult
            {
                PlanId = plan.Id,
                Severity = ValidationSeverity.Error,
                Code = ValidationCode.PointsChanged,
                Message = $"La puntuación total adaptada ({totalAdapted}) no coincide con el original ({assessment.TotalPoints}).",
                OriginalValue = assessment.TotalPoints.ToString(),
                AdaptedValue = totalAdapted.ToString(),
                RequiresReview = true
            });
        }

        foreach (var adapted in adaptedQuestions)
        {
            if (!questionsById.TryGetValue(adapted.QuestionId, out var original)) continue;
            if (adapted.Points != original.Points)
            {
                results.Add(new ValidationResult
                {
                    PlanId = plan.Id,
                    Severity = ValidationSeverity.Error,
                    Code = ValidationCode.PointsChanged,
                    QuestionId = original.Id,
                    Message = "Los puntos de la pregunta cambiaron respecto al original.",
                    OriginalValue = original.Points.ToString(),
                    AdaptedValue = adapted.Points.ToString(),
                    RequiresReview = true
                });
            }
        }
    }

    private static void ValidateContentCoverage(
        AdaptationPlan plan, AdaptedQuestion adapted, Question original, List<ValidationResult> results)
    {
        if (original.ConstructTags.Count == 0) return;

        var coverage = TextHeuristics.KeywordCoverage(original.OriginalText, adapted.AdaptedText);
        if (coverage < 0.3)
        {
            results.Add(new ValidationResult
            {
                PlanId = plan.Id,
                Severity = ValidationSeverity.Error,
                Code = ValidationCode.ContentDropped,
                QuestionId = original.Id,
                Message = $"Solo el {coverage:P0} de las palabras clave del original aparece en la versión adaptada; puede haberse perdido contenido necesario.",
                RequiresReview = true
            });
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
            results.Add(new ValidationResult
            {
                PlanId = plan.Id,
                Severity = ValidationSeverity.Error,
                Code = ValidationCode.ReadingConstructBypassed,
                QuestionId = original.Id,
                Message = "Se aplicó lectura en voz alta/audio en una pregunta cuyo constructo es la lectura.",
                RequiresReview = true
            });
        }

        if (original.ConstructTags.Contains(ConstructTags.Spelling) &&
            appliedRuleIds.Contains("dysorthography.word_bank_corrector"))
        {
            results.Add(new ValidationResult
            {
                PlanId = plan.Id,
                Severity = ValidationSeverity.Error,
                Code = ValidationCode.SpellingConstructBypassed,
                QuestionId = original.Id,
                Message = "Se activó un banco de palabras/corrector en una pregunta cuyo constructo es la ortografía.",
                RequiresReview = true
            });
        }
    }

    private static void ValidateHintRevealsAnswer(
        AdaptationPlan plan, AdaptedQuestion adapted, Question original, List<ValidationResult> results)
    {
        var hintText = string.Join(" ", adapted.Supports.Append(adapted.AdaptedText));
        if (TextHeuristics.ContainsAnswer(hintText, original.ExpectedAnswer))
        {
            results.Add(new ValidationResult
            {
                PlanId = plan.Id,
                Severity = ValidationSeverity.Error,
                Code = ValidationCode.HintRevealsAnswer,
                QuestionId = original.Id,
                Message = "Un ejemplo, apoyo o glosario generado contiene la respuesta esperada.",
                RequiresReview = true
            });
        }
    }

    private static void ValidateDifficultyReduced(
        AdaptationPlan plan, AdaptedQuestion adapted, Question original, List<ValidationResult> results)
    {
        var originalWords = TextHeuristics.WordCount(original.OriginalText);
        var adaptedWords = TextHeuristics.WordCount(adapted.AdaptedText);
        if (originalWords >= 8 && adaptedWords < originalWords * 0.4)
        {
            results.Add(new ValidationResult
            {
                PlanId = plan.Id,
                Severity = ValidationSeverity.Warning,
                Code = ValidationCode.DifficultyReduced,
                QuestionId = original.Id,
                Message = "El texto adaptado es mucho más corto que el original; revisa que no se haya reducido la exigencia.",
                RequiresReview = false
            });
        }
    }

    private static void ValidateCurricularChange(AdaptationPlan plan, List<ValidationResult> results)
    {
        if (!plan.IsCurricularChange) return;

        results.Add(new ValidationResult
        {
            PlanId = plan.Id,
            Severity = ValidationSeverity.Review,
            Code = ValidationCode.CurricularChange,
            Message = "Cambio curricular activado explícitamente por el docente. Requiere aprobación obligatoria antes de exportar.",
            RequiresReview = true
        });
    }

    private static void ValidateExtractionConfidence(
        AdaptationPlan plan, double? extractionConfidence, List<ValidationResult> results)
    {
        if (extractionConfidence.HasValue && extractionConfidence.Value < 0.6)
        {
            results.Add(new ValidationResult
            {
                PlanId = plan.Id,
                Severity = ValidationSeverity.Review,
                Code = ValidationCode.LowExtractionConfidence,
                Message = $"La extracción del documento original tiene confianza baja ({extractionConfidence:P0}). Compara con el archivo original antes de aprobar.",
                RequiresReview = true
            });
        }
    }

    private static void ValidateUnsupportedConflicts(AdaptationPlan plan, List<ValidationResult> results)
    {
        foreach (var warning in plan.Warnings.Where(w => w.StartsWith("UNSUPPORTED_CONFLICT")))
        {
            results.Add(new ValidationResult
            {
                PlanId = plan.Id,
                Severity = ValidationSeverity.Review,
                Code = ValidationCode.UnsupportedConflict,
                Message = warning,
                RequiresReview = true
            });
        }
    }
}
