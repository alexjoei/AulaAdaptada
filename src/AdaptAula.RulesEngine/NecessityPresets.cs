using AdaptAula.Domain;

namespace AdaptAula.RulesEngine;

/// <summary>
/// Needs (diagnosis-like labels) as named bundles of atomic measures, each with a classification
/// (Recommended / Optional / RequiresTeacherDecision / NotRecommended). Loaded from the measure library data file.
/// A need is only ever a starting point: the unit the engine really works with is the individual measure.
/// </summary>
public static class NecessityPresets
{
    public static IReadOnlyList<NecessityPreset> All => MeasureLibrary.Default.Needs;

    public static IReadOnlyDictionary<string, NecessityPreset> ByKey => MeasureLibrary.Default.NeedsByKey;

    /// <summary>Needs offered in the picker (legacy/hidden ones such as "curricular" are excluded).</summary>
    public static IReadOnlyList<NecessityPreset> Visible => All.Where(n => !n.Hidden).ToList();
}
