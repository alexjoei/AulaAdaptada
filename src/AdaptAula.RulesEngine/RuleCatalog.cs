using AdaptAula.Domain;

namespace AdaptAula.RulesEngine;

/// <summary>
/// The atomic measure catalog. The data lives in <c>Data/measure-library.json</c> (see <see cref="MeasureLibrary"/>);
/// this class is only a convenient static view over it. Nothing here is branched on a profile/diagnosis in code.
/// </summary>
public static class RuleCatalog
{
    public static IReadOnlyList<AdaptationRule> All => MeasureLibrary.Default.Rules;

    public static IReadOnlyDictionary<string, AdaptationRule> ById => MeasureLibrary.Default.RulesById;
}
