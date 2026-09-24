using System.Text.RegularExpressions;

namespace AdaptAula.Validation;

/// <summary>Small, deliberately simple text heuristics — good enough for "validación básica"
/// (spec roadmap MVP1). None of these are AI calls; they run deterministically over the
/// AI's output so the safety checks stay fully auditable.</summary>
internal static class TextHeuristics
{
    private static readonly Regex WordPattern = new(@"[\p{L}0-9]{4,}", RegexOptions.Compiled);

    public static List<string> SignificantWords(string text) =>
        WordPattern.Matches(text ?? string.Empty)
            .Select(m => m.Value.ToLowerInvariant())
            .Distinct()
            .ToList();

    /// <summary>Fraction of the original's significant words that still appear somewhere in the adapted text.
    /// Used as a coarse proxy for "was necessary content dropped" — not a substitute for teacher review.</summary>
    public static double KeywordCoverage(string originalText, string adaptedText)
    {
        var originalWords = SignificantWords(originalText);
        if (originalWords.Count == 0) return 1.0;

        var adaptedWords = new HashSet<string>(SignificantWords(adaptedText));
        var covered = originalWords.Count(w => adaptedWords.Contains(w));
        return (double)covered / originalWords.Count;
    }

    public static int WordCount(string text) =>
        string.IsNullOrWhiteSpace(text) ? 0 : text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    public static bool ContainsAnswer(string haystack, string? expectedAnswer)
    {
        if (string.IsNullOrWhiteSpace(expectedAnswer) || string.IsNullOrWhiteSpace(haystack))
            return false;

        var normalizedAnswer = expectedAnswer.Trim();
        if (normalizedAnswer.Length < 3) return false; // too short to be a meaningful leak signal

        return haystack.Contains(normalizedAnswer, StringComparison.OrdinalIgnoreCase);
    }
}
