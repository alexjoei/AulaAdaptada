using AdaptAula.Domain;

namespace AdaptAula.Infrastructure.Export;

/// <summary>Plan-wide layout flags derived from which atomic rules were actually applied
/// somewhere in the plan (spec §4/§5: dyslexia/ADHD/etc. typography and layout rules). The
/// exporter only ever renders what the resolver decided — it never adds styling on its own.</summary>
public record ExportStyle(bool LargeAccessibleFont, bool LeftAlignLowDensity, bool ShowSupportsAsChecklist)
{
    public static ExportStyle From(AdaptationPlan plan)
    {
        var appliedRuleIds = plan.ResolvedRulesByQuestion.Values
            .SelectMany(list => list)
            .Where(r => r.Applied)
            .Select(r => r.RuleId)
            .ToHashSet();

        return new ExportStyle(
            LargeAccessibleFont: appliedRuleIds.Contains("dyslexia.typography_sans_serif") || appliedRuleIds.Contains("low_vision.configurable_size_contrast"),
            LeftAlignLowDensity: appliedRuleIds.Contains("dyslexia.left_align_low_density") || appliedRuleIds.Contains("slow_processing.lower_density_per_page"),
            ShowSupportsAsChecklist: appliedRuleIds.Contains("adhd.progress_checklist") || appliedRuleIds.Contains("executive_functions.checklist_steps_progress"));
    }
}
