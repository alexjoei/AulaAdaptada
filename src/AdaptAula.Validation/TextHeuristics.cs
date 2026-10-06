using System.Text.RegularExpressions;

namespace AdaptAula.Validation;

/// <summary>Small, deliberately simple text heuristics — good enough for "validación básica"
/// (spec roadmap MVP1). None of these are AI calls; they run deterministically over the
/// AI's output so the safety checks stay fully auditable.</summary>
public static class TextHeuristics
{
    private static readonly Regex WordPattern = new(@"[\p{L}0-9]{4,}", RegexOptions.Compiled);
    private static readonly Regex AnyWord = new(@"\p{L}+", RegexOptions.Compiled);

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

    /// <summary>Share of the answer's significant words that show up in <paramref name="hintText"/> but were NOT already
    /// in the question — a partial leak that an exact-match check misses (e.g. the answer reworded or split in two).</summary>
    public static double AnswerLeakRatio(string hintText, string questionText, string? expectedAnswer)
    {
        if (string.IsNullOrWhiteSpace(expectedAnswer)) return 0;
        var answerWords = SignificantWords(expectedAnswer);
        if (answerWords.Count < 2) return 0;

        var inQuestion = new HashSet<string>(SignificantWords(questionText));
        var newWords = answerWords.Where(w => !inQuestion.Contains(w)).ToList();
        if (newWords.Count < 2) return 0;

        var inHint = new HashSet<string>(SignificantWords(hintText));
        return (double)newWords.Count(inHint.Contains) / newWords.Count;
    }

    public static bool ContainsTerm(string text, string term) =>
        !string.IsNullOrWhiteSpace(term) && text.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase);

    private static readonly Dictionary<string, HashSet<string>> StopWords = new()
    {
        ["es"] = new(StringComparer.OrdinalIgnoreCase) { "el", "la", "los", "las", "de", "del", "que", "y", "en", "un", "una", "por", "con", "para", "es", "se", "al", "lo", "como", "más", "pero", "su", "sus", "este", "esta", "qué", "cuál", "escribe", "explica" },
        ["en"] = new(StringComparer.OrdinalIgnoreCase) { "the", "of", "and", "to", "in", "is", "that", "it", "for", "with", "as", "was", "are", "this", "what", "which", "write", "explain", "your", "you", "an", "be", "on", "by" },
        ["ca"] = new(StringComparer.OrdinalIgnoreCase) { "el", "la", "els", "les", "de", "del", "que", "i", "en", "un", "una", "per", "amb", "és", "es", "al", "com", "més", "però", "seu", "aquest", "aquesta", "què", "explica", "escriu" },
        ["fr"] = new(StringComparer.OrdinalIgnoreCase) { "le", "la", "les", "de", "des", "du", "et", "en", "un", "une", "pour", "avec", "est", "que", "qui", "dans", "sur", "ce", "cette", "quel", "explique", "écris" }
    };

    /// <summary>Best-effort language of a text from stop-word frequency; null when there isn't enough signal to tell.
    /// Deliberately conservative: a short numeric answer must never be reported as "a different language".</summary>
    public static string? DetectLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var words = AnyWord.Matches(text).Select(m => m.Value).ToList();
        if (words.Count < 6) return null;

        var scores = StopWords.ToDictionary(kv => kv.Key, kv => words.Count(w => kv.Value.Contains(w)));
        var ordered = scores.OrderByDescending(kv => kv.Value).ToList();
        var best = ordered[0];
        if (best.Value < 3) return null;
        if (ordered.Count > 1 && ordered[1].Value * 1.5 >= best.Value) return null; // ambiguous (es vs ca)
        return best.Key;
    }
}
