using AdaptAula.Domain;

namespace AdaptAula.Validation;

public class QuestionSemaphore
{
    public Guid QuestionId { get; set; }
    public SemaphoreLevel Level { get; set; }
    public List<string> Reasons { get; set; } = new();
}

public class SemaphoreReport
{
    public SemaphoreLevel Overall { get; set; }
    public List<string> OverallReasons { get; set; } = new();
    public List<QuestionSemaphore> Questions { get; set; } = new();
}

/// <summary>
/// Pedagogical traffic light (V2 §16). Green: a safe adaptation. Orange: review it — a hint, a format change or a
/// linguistic reduction. Red: it may have changed the criterion, answer, score, content or cognitive level.
/// Derived purely from the validation results and the applied rules, so it is fully explainable.
/// </summary>
public static class PedagogicalSemaphore
{
    public static SemaphoreReport Compute(
        AdaptationPlan plan,
        IReadOnlyList<AdaptedQuestion> adaptedQuestions,
        IReadOnlyList<ValidationResult> validation,
        Func<string, AdaptationRule?> lookupRule)
    {
        var report = new SemaphoreReport();

        foreach (var adapted in adaptedQuestions)
        {
            var q = new QuestionSemaphore { QuestionId = adapted.QuestionId, Level = SemaphoreLevel.Green };

            foreach (var v in validation.Where(v => v.QuestionId == adapted.QuestionId))
            {
                if (v.Severity == ValidationSeverity.Error) Raise(q, SemaphoreLevel.Red, v.Message);
                else Raise(q, SemaphoreLevel.Orange, v.Message);
            }

            if (adapted.Proposal is { Status: ProposalStatus.Pending })
                Raise(q, SemaphoreLevel.Orange, "Hay un cambio propuesto por la IA pendiente de tu decisión.");

            if (plan.ResolvedRulesByQuestion.TryGetValue(adapted.QuestionId, out var resolved))
            {
                foreach (var r in resolved.Where(r => r.Applied))
                {
                    var rule = lookupRule(r.RuleId);
                    if (rule is null || rule.Kind is MeasureKind.Style or MeasureKind.Logistics) continue;
                    if (!HasVisibleEffect(adapted, r.RuleId)) continue;

                    if (rule.RequiresTeacherReview || rule.RiskLevel >= RiskLevel.Medium)
                        Raise(q, SemaphoreLevel.Orange, $"Medida aplicada que conviene revisar: {rule.Description}.");
                }
            }

            report.Questions.Add(q);
        }

        // Plan-level findings (no question attached), e.g. a curricular change or a missing question.
        foreach (var v in validation.Where(v => v.QuestionId is null))
        {
            if (v.Severity == ValidationSeverity.Error) Raise(report, SemaphoreLevel.Red, v.Message);
            else if (v.Severity == ValidationSeverity.Review) Raise(report, SemaphoreLevel.Orange, v.Message);
        }

        foreach (var q in report.Questions)
        {
            if (q.Level == SemaphoreLevel.Red) Raise(report, SemaphoreLevel.Red, "Hay preguntas en rojo.");
            else if (q.Level == SemaphoreLevel.Orange) Raise(report, SemaphoreLevel.Orange, "Hay preguntas que conviene revisar.");
        }

        report.OverallReasons = report.OverallReasons.Distinct().ToList();
        return report;
    }

    /// <summary>An applied rule only deserves a flag if the text/supports actually moved; a question left untouched is Green.</summary>
    private static bool HasVisibleEffect(AdaptedQuestion adapted, string ruleId) =>
        adapted.ChangeLog.Any(c => c.RuleId == ruleId) || adapted.Supports.Count > 0;

    private static void Raise(QuestionSemaphore q, SemaphoreLevel level, string reason)
    {
        if (level > q.Level) q.Level = level;
        if (!q.Reasons.Contains(reason)) q.Reasons.Add(reason);
    }

    private static void Raise(SemaphoreReport report, SemaphoreLevel level, string reason)
    {
        if (level > report.Overall) report.Overall = level;
        report.OverallReasons.Add(reason);
    }
}
