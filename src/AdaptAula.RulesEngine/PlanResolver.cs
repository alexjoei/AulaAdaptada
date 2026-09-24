using AdaptAula.Domain;

namespace AdaptAula.RulesEngine;

public class PlanResolverInput
{
    public required Assessment Assessment { get; init; }
    public required StudentProfile Profile { get; init; }
    public int Level { get; init; } = 1;

    /// <summary>Only set when the teacher has explicitly opened the curricular-adaptation path
    /// and supplied an objective. The resolver never turns this on by itself (spec §1, §5).</summary>
    public bool CurricularChangeAuthorized { get; init; }
    public string? CurricularObjective { get; init; }
}

/// <summary>
/// Deterministic PLAN step (spec §7 PLAN, §6 Lógica de combinación de perfiles). Produces an
/// <see cref="AdaptationPlan"/> — which atomic rules apply, per question — without rewriting
/// any text. Precedence, highest first: teacher LOCK > individual measure > construct
/// protection > necessity preset > global style. Conflicts are never resolved silently.
/// </summary>
public static class PlanResolver
{
    public static AdaptationPlan Resolve(PlanResolverInput input)
    {
        var plan = new AdaptationPlan
        {
            AssessmentId = input.Assessment.Id,
            ProfileId = input.Profile.Id,
            Level = input.Level,
            Locks = new List<string>(input.Assessment.LockedFields),
            IsCurricularChange = input.CurricularChangeAuthorized && !string.IsNullOrWhiteSpace(input.CurricularObjective),
            CurricularObjective = input.CurricularObjective
        };

        var candidateRuleIds = BuildCandidateRuleIds(input.Profile);

        foreach (var section in input.Assessment.Sections)
        {
            foreach (var question in section.Questions)
            {
                var resolved = ResolveForQuestion(question, candidateRuleIds, input, plan.Warnings);
                plan.ResolvedRulesByQuestion[question.Id] = resolved;
            }
        }

        if (plan.IsCurricularChange)
        {
            plan.Warnings.Add("CURRICULAR_CHANGE: cambio curricular autorizado por el docente; requiere aprobación obligatoria.");
            plan.Status = PlanStatus.NeedsTeacherReview;
        }
        else
        {
            plan.Status = PlanStatus.Planned;
        }

        return plan;
    }

    /// <summary>Union of every necessity preset's rule ids for this profile, plus individual
    /// accommodations, minus explicit exceptions (spec §6 precedence: medida individual > preset).</summary>
    private static HashSet<string> BuildCandidateRuleIds(StudentProfile profile)
    {
        var ids = new HashSet<string>();

        foreach (var measureKey in profile.Measures)
        {
            if (NecessityPresets.ByKey.TryGetValue(measureKey, out var preset))
            {
                foreach (var ruleId in preset.RuleIds)
                    ids.Add(ruleId);
            }
        }

        foreach (var accommodationRuleId in profile.Accommodations)
        {
            if (RuleCatalog.ById.ContainsKey(accommodationRuleId))
                ids.Add(accommodationRuleId);
        }

        foreach (var exceptionRuleId in profile.Exceptions)
            ids.Remove(exceptionRuleId);

        return ids;
    }

    private static List<ResolvedRule> ResolveForQuestion(
        Question question, HashSet<string> candidateRuleIds, PlanResolverInput input, List<string> planWarnings)
    {
        var results = new List<ResolvedRule>();
        var appliedRuleIds = new HashSet<string>();

        foreach (var ruleId in candidateRuleIds)
        {
            if (!RuleCatalog.ById.TryGetValue(ruleId, out var rule))
                continue;

            if (rule.MinLevel > input.Level)
            {
                results.Add(new ResolvedRule
                {
                    RuleId = ruleId,
                    Source = RuleSource.NecessityPreset,
                    Applied = false,
                    Reason = $"Requiere nivel {rule.MinLevel}, plan está en nivel {input.Level}."
                });
                continue;
            }

            var isIndividualMeasure = input.Profile.Accommodations.Contains(ruleId);
            var source = isIndividualMeasure ? RuleSource.IndividualMeasure : RuleSource.NecessityPreset;

            // Construct protection always beats a preset/individual measure (priority 3 > 4/2 for this case
            // specifically per spec: "Impide apoyo que invalide constructo").
            var blockedByConstruct = rule.BlockedByConstructTags.Any(question.ConstructTags.Contains);
            if (blockedByConstruct)
            {
                results.Add(new ResolvedRule
                {
                    RuleId = ruleId,
                    Source = RuleSource.ConstructProtection,
                    Applied = false,
                    Reason = $"Bloqueada: el constructo de esta pregunta ({string.Join(",", rule.BlockedByConstructTags.Where(question.ConstructTags.Contains))}) coincide con la protección de la regla."
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
                Reason = rule.ApplyMode == ApplyMode.Always ? "Regla siempre activa (guardrail)." : "Aplicada."
            });
        }

        ResolveConflicts(results, appliedRuleIds, question, planWarnings);

        return results.OrderByDescending(r => r.Applied).ThenBy(r => r.RuleId).ToList();
    }

    private static void ResolveConflicts(
        List<ResolvedRule> results, HashSet<string> appliedRuleIds, Question question, List<string> planWarnings)
    {
        foreach (var resolved in results.Where(r => r.Applied).ToList())
        {
            if (!RuleCatalog.ById.TryGetValue(resolved.RuleId, out var rule) || rule.ConflictsWith.Count == 0)
                continue;

            foreach (var conflictId in rule.ConflictsWith)
            {
                if (!appliedRuleIds.Contains(conflictId))
                    continue;

                var other = results.First(r => r.RuleId == conflictId);
                var otherRule = RuleCatalog.ById[conflictId];

                // Keep whichever preserves the construct better (spec §6): lower risk wins;
                // never decide silently — always emit a warning either way.
                var keepThis = rule.RiskLevel <= otherRule.RiskLevel;
                var loser = keepThis ? other : resolved;
                loser.Applied = false;
                loser.Reason = $"UNSUPPORTED_CONFLICT con '{(keepThis ? resolved.RuleId : other.RuleId)}'; se mantuvo la regla de menor riesgo.";
                appliedRuleIds.Remove(loser.RuleId);

                planWarnings.Add(
                    $"UNSUPPORTED_CONFLICT en pregunta {question.Id}: '{resolved.RuleId}' vs '{conflictId}' — se aplicó solo '{(keepThis ? resolved.RuleId : conflictId)}'.");
            }
        }
    }
}
