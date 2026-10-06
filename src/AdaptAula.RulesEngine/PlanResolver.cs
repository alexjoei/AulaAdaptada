using AdaptAula.Domain;

namespace AdaptAula.RulesEngine;

public class PlanResolverInput
{
    public required Assessment Assessment { get; init; }
    public required StudentProfile Profile { get; init; }
    public int Level { get; init; } = 1;

    /// <summary>True only when the teacher flipped «¿Existe adaptación curricular?» (V2 §14). The resolver never
    /// turns this on by itself, and without it the plan can never exceed level 2.</summary>
    public bool CurricularChangeAuthorized { get; init; }
    public string? CurricularObjective { get; init; }
    public string? CurricularReference { get; init; }
    public IReadOnlyList<string> CurricularCriteriaIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CurricularContentIds { get; init; } = Array.Empty<string>();

    /// <summary>Measures the teacher defined herself (stored in the database, V2 §3).</summary>
    public IReadOnlyList<AdaptationRule> ExtraRules { get; init; } = Array.Empty<AdaptationRule>();
}

/// <summary>
/// Deterministic PLAN step (spec §7 PLAN, §6 Lógica de combinación de perfiles). Produces an
/// <see cref="AdaptationPlan"/> — which atomic rules apply, per question — without rewriting any text.
/// Precedence, highest first: teacher LOCK > individual measure > construct protection > need preset > global style.
/// Conflicts are never resolved silently, and an individual measure always beats a preset in a conflict (V2 §18).
/// </summary>
public static class PlanResolver
{
    public static AdaptationPlan Resolve(PlanResolverInput input)
    {
        var extra = input.ExtraRules.ToDictionary(r => r.Id);
        AdaptationRule? Lookup(string id) =>
            RuleCatalog.ById.TryGetValue(id, out var rule) ? rule : extra.GetValueOrDefault(id);

        var criteriaIds = input.CurricularCriteriaIds.ToList();
        var contentIds = input.CurricularContentIds.ToList();
        var curricular = input.CurricularChangeAuthorized &&
                         (!string.IsNullOrWhiteSpace(input.CurricularObjective) || criteriaIds.Count > 0);

        var warnings = new List<string>();

        // Level 3 (curricular adaptation) exists only behind the teacher's explicit switch; the AI/engine
        // never lowers the level by itself.
        var level = input.Level;
        if (!input.CurricularChangeAuthorized && level > 2)
        {
            level = 2;
            warnings.Add("NIVEL_LIMITADO: el nivel 3 (adaptación curricular) solo se activa con el interruptor «¿Existe adaptación curricular?»; se usó el nivel 2.");
        }

        var plan = new AdaptationPlan
        {
            AssessmentId = input.Assessment.Id,
            ProfileId = input.Profile.Id,
            Level = level,
            Locks = new List<string>(input.Assessment.LockedFields),
            IsCurricularChange = curricular,
            CurricularObjective = input.CurricularObjective,
            CurricularReference = input.CurricularReference,
            CurricularCriteriaIds = criteriaIds,
            CurricularContentIds = contentIds,
            ProfileSettings = new Dictionary<string, string>(input.Profile.Settings),
            Warnings = warnings
        };

        var effective = MeasureSelection.Compute(
            input.Profile.Measures, input.Profile.Accommodations, input.Profile.Exceptions, input.Profile.SchemaVersion, extra);
        var enabled = effective.Where(e => e.Enabled && Lookup(e.RuleId) is not null).ToList();

        foreach (var measure in enabled.Where(e => e.Warning is not null && e.Origin == "individual"))
            warnings.Add($"MEDIDA_NO_RECOMENDADA: «{Lookup(measure.RuleId)!.Description}» — {measure.Warning}");

        var individualIds = input.Profile.Accommodations.ToHashSet();

        foreach (var section in input.Assessment.Sections)
        {
            foreach (var question in section.Questions)
            {
                plan.ResolvedRulesByQuestion[question.Id] =
                    ResolveForQuestion(question, enabled, individualIds, Lookup, level, plan.Warnings);
            }
        }

        var appliedRules = plan.ResolvedRulesByQuestion.Values
            .SelectMany(list => list)
            .Where(r => r.Applied)
            .Select(r => r.RuleId)
            .Distinct()
            .Select(Lookup)
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();
        plan.Style = DocumentStyleResolver.Resolve(appliedRules, plan.ProfileSettings);

        if (plan.IsCurricularChange)
        {
            plan.Warnings.Add("CURRICULAR_CHANGE: cambio curricular autorizado por el docente; requiere aprobación obligatoria.");
            plan.Status = PlanStatus.NeedsTeacherReview;
        }
        else
        {
            if (input.CurricularChangeAuthorized)
                plan.Warnings.Add("CURRICULAR_SIN_REFERENTES: activaste la adaptación curricular pero no indicaste criterios ni objetivo; no se aplicó ningún cambio curricular.");
            plan.Status = PlanStatus.Planned;
        }

        return plan;
    }

    private static List<ResolvedRule> ResolveForQuestion(
        Question question, List<EffectiveMeasure> enabled, HashSet<string> individualIds,
        Func<string, AdaptationRule?> lookup, int level, List<string> planWarnings)
    {
        var results = new List<ResolvedRule>();
        var appliedRuleIds = new HashSet<string>();

        foreach (var measure in enabled)
        {
            var ruleId = measure.RuleId;
            var rule = lookup(ruleId)!;

            var isIndividualMeasure = individualIds.Contains(ruleId);
            var source = isIndividualMeasure ? RuleSource.IndividualMeasure : RuleSource.NecessityPreset;

            if (rule.MinLevel > level)
            {
                results.Add(new ResolvedRule
                {
                    RuleId = ruleId,
                    Source = RuleSource.NecessityPreset,
                    Applied = false,
                    Reason = $"Requiere nivel {rule.MinLevel}, plan está en nivel {level}."
                });
                continue;
            }

            // Construct protection always beats a preset/individual measure.
            var blockedTags = rule.BlockedByConstructTags.Where(question.ConstructTags.Contains).ToList();
            if (blockedTags.Count > 0)
            {
                results.Add(new ResolvedRule
                {
                    RuleId = ruleId,
                    Source = RuleSource.ConstructProtection,
                    Applied = false,
                    Reason = $"Bloqueada: el constructo de esta pregunta ({string.Join(",", blockedTags)}) coincide con la protección de la regla."
                });
                continue;
            }

            if (rule.ApplyMode == ApplyMode.Never && !isIndividualMeasure)
            {
                results.Add(new ResolvedRule
                {
                    RuleId = ruleId,
                    Source = source,
                    Applied = false,
                    Reason = "Regla no-por-defecto; requiere autorización explícita como medida individual."
                });
                continue;
            }

            appliedRuleIds.Add(ruleId);
            results.Add(new ResolvedRule
            {
                RuleId = ruleId,
                Source = source,
                Applied = true,
                ProposalOnly = rule.AltersAssessedConstruct,
                Reason = rule.ApplyMode == ApplyMode.Always
                    ? "Regla siempre activa (guardrail)."
                    : rule.AltersAssessedConstruct
                        ? "Puede cambiar lo que se evalúa: la IA solo la propone, tú decides."
                        : "Aplicada."
            });
        }

        ResolveConflicts(results, appliedRuleIds, question, lookup, planWarnings);

        return results.OrderByDescending(r => r.Applied).ThenBy(r => r.RuleId, StringComparer.Ordinal).ToList();
    }

    private static void ResolveConflicts(
        List<ResolvedRule> results, HashSet<string> appliedRuleIds, Question question,
        Func<string, AdaptationRule?> lookup, List<string> planWarnings)
    {
        foreach (var resolved in results.Where(r => r.Applied).ToList())
        {
            if (!resolved.Applied) continue; // may have lost an earlier conflict in this same loop
            var rule = lookup(resolved.RuleId)!;
            if (rule.ConflictsWith.Count == 0) continue;

            foreach (var conflictId in rule.ConflictsWith)
            {
                if (!appliedRuleIds.Contains(conflictId)) continue;

                var other = results.First(r => r.RuleId == conflictId);
                var otherRule = lookup(conflictId)!;

                // Individual measure beats a preset (V2 §18); otherwise whichever preserves the construct
                // better (lower risk) is kept. Never silent: always emits a warning.
                bool keepThis;
                if (resolved.Source == RuleSource.IndividualMeasure && other.Source != RuleSource.IndividualMeasure) keepThis = true;
                else if (other.Source == RuleSource.IndividualMeasure && resolved.Source != RuleSource.IndividualMeasure) keepThis = false;
                else keepThis = rule.RiskLevel <= otherRule.RiskLevel;

                var loser = keepThis ? other : resolved;
                loser.Applied = false;
                loser.Reason = $"UNSUPPORTED_CONFLICT con '{(keepThis ? resolved.RuleId : other.RuleId)}'; se mantuvo {(resolved.Source == RuleSource.IndividualMeasure || other.Source == RuleSource.IndividualMeasure ? "la medida individual" : "la regla de menor riesgo")}.";
                appliedRuleIds.Remove(loser.RuleId);

                planWarnings.Add(
                    $"UNSUPPORTED_CONFLICT en pregunta {question.Id}: '{resolved.RuleId}' vs '{conflictId}' — se aplicó solo '{(keepThis ? resolved.RuleId : conflictId)}'.");
                if (!keepThis) break;
            }
        }
    }
}
