using AdaptAula.Domain;

namespace AdaptAula.RulesEngine;

/// <summary>One measure as it stands for a given set of needs + individual choices (V2 §2, §3, §18).</summary>
public class EffectiveMeasure
{
    public string RuleId { get; set; } = string.Empty;

    /// <summary>The classification after merging every selected need that mentions this measure.</summary>
    public MeasureClassification Classification { get; set; }

    /// <summary>Whether the measure is switched on for this profile.</summary>
    public bool Enabled { get; set; }

    /// <summary>need | individual — where the "on" came from. Individual choices beat presets (V2 §18).</summary>
    public string Origin { get; set; } = "need";

    /// <summary>Needs that mention the measure (a measure two needs share appears once — duplicates removed).</summary>
    public List<string> NeedKeys { get; set; } = new();

    /// <summary>Needs disagreed (one recommends, another advises against), so the teacher must decide.</summary>
    public bool ClassificationConflict { get; set; }

    public string? Warning { get; set; }
}

/// <summary>
/// Merges the measures of several needs into one effective list: atomic measures, no duplicates, conflicts surfaced
/// instead of silently resolved (V2 §18). Used by both the profile editor and <see cref="PlanResolver"/> so the
/// teacher sees exactly what the engine will apply.
/// </summary>
public static class MeasureSelection
{
    public static List<EffectiveMeasure> Compute(
        IEnumerable<string> needKeys,
        IEnumerable<string> accommodations,
        IEnumerable<string> exceptions,
        int schemaVersion = 2,
        IReadOnlyDictionary<string, AdaptationRule>? extraRules = null)
    {
        var byRule = new Dictionary<string, List<(string Need, MeasureClassification Classification)>>();
        foreach (var key in needKeys.Distinct())
        {
            if (!NecessityPresets.ByKey.TryGetValue(key, out var need)) continue;
            foreach (var measure in need.Measures)
            {
                if (!byRule.TryGetValue(measure.RuleId, out var list))
                    byRule[measure.RuleId] = list = new();
                list.Add((key, measure.Classification));
            }
        }

        var accommodationSet = accommodations.ToHashSet();
        var exceptionSet = exceptions.ToHashSet();

        foreach (var id in accommodationSet)
        {
            if (!byRule.ContainsKey(id) && Exists(id, extraRules))
                byRule[id] = new();
        }

        var result = new List<EffectiveMeasure>();
        foreach (var (ruleId, mentions) in byRule.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var (classification, conflict) = Merge(mentions.Select(m => m.Classification).ToList());
            var individual = accommodationSet.Contains(ruleId);
            var recommendedByNeed = schemaVersion < 2
                ? classification != MeasureClassification.NotRecommended
                : classification == MeasureClassification.Recommended && !conflict;
            var enabled = (individual || recommendedByNeed) && !exceptionSet.Contains(ruleId);

            string? warning = null;
            if (enabled && individual && classification == MeasureClassification.NotRecommended)
                warning = "Esta medida no está recomendada para las necesidades seleccionadas; revisa que no cambie lo que se evalúa.";
            else if (conflict)
                warning = "Las necesidades seleccionadas no coinciden en esta medida (una la recomienda, otra la desaconseja): decídelo tú.";

            result.Add(new EffectiveMeasure
            {
                RuleId = ruleId,
                Classification = classification,
                Enabled = enabled,
                Origin = individual ? "individual" : "need",
                NeedKeys = mentions.Select(m => m.Need).Distinct().ToList(),
                ClassificationConflict = conflict,
                Warning = warning
            });
        }

        return result;
    }

    private static bool Exists(string id, IReadOnlyDictionary<string, AdaptationRule>? extra) =>
        RuleCatalog.ById.ContainsKey(id) || (extra?.ContainsKey(id) ?? false);

    /// <summary>Recommended beats Optional/Decision; a Recommended + NotRecommended clash is flagged for the teacher.</summary>
    public static (MeasureClassification Classification, bool Conflict) Merge(IReadOnlyList<MeasureClassification> mentions)
    {
        if (mentions.Count == 0) return (MeasureClassification.Optional, false);

        var hasRecommended = mentions.Contains(MeasureClassification.Recommended);
        var hasNot = mentions.Contains(MeasureClassification.NotRecommended);

        if (hasRecommended && hasNot) return (MeasureClassification.RequiresTeacherDecision, true);
        if (hasRecommended) return (MeasureClassification.Recommended, false);
        if (mentions.Contains(MeasureClassification.RequiresTeacherDecision)) return (MeasureClassification.RequiresTeacherDecision, false);
        if (mentions.Contains(MeasureClassification.Optional)) return (MeasureClassification.Optional, false);
        return (MeasureClassification.NotRecommended, false);
    }
}
