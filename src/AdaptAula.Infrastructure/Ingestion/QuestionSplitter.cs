using System.Text.RegularExpressions;
using AdaptAula.Domain;

namespace AdaptAula.Infrastructure.Ingestion;

/// <summary>
/// Best-effort PARSE step (spec §7): turns raw extracted text into a starting Assessment
/// structure. This is intentionally simple — the Analysis screen is where the teacher confirms
/// constructs, points and locks (ANNOTATE), so the splitter only needs to get the document into
/// a reasonable starting shape, not a perfect one.
/// </summary>
public static class QuestionSplitter
{
    private static readonly Regex NumberedQuestion = new(@"(?m)^[ \t]*(?<num>\d{1,2})[ \t]*[\.\)·-][ \t]*(?<rest>.*)$", RegexOptions.Compiled);
    private static readonly Regex PointsHint = new(@"(?<points>\d+)\s*(points?|puntos?)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Real question numbers in a printed test are spaced apart by a full question's worth of
    /// text. Numbers that appear closer together than this are almost always something else that
    /// happens to start with a digit — a true/false table's row labels ("1. Susan is 13 years
    /// old", "2. ...") being the classic case — so they're excluded before we even consider them
    /// as question boundaries.
    /// </summary>
    private const int MinBlockLength = 60;

    public static (List<Question> Questions, double Confidence) Split(string text)
    {
        text = text.Replace("\r\n", "\n").Trim();
        var boundaries = FindQuestionBoundaries(text);

        if (boundaries.Count >= 2)
        {
            var questions = new List<Question>();
            for (var i = 0; i < boundaries.Count; i++)
            {
                var start = boundaries[i].Index;
                var end = i + 1 < boundaries.Count ? boundaries[i + 1].Index : text.Length;
                var block = text[start..end].Trim();
                questions.Add(ToQuestion(block, i));
            }
            return (questions, 0.85);
        }

        var paragraphs = text.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .ToList();

        if (paragraphs.Count > 1)
        {
            var questions = paragraphs.Select((p, i) => ToQuestion(p, i)).ToList();
            return (questions, 0.5);
        }

        // Single unstructured blob — still return it as one question so the teacher has something
        // to start annotating, but flag low confidence (LOW_EXTRACTION_CONFIDENCE).
        return (new List<Question> { ToQuestion(text, 0) }, 0.25);
    }

    /// <summary>
    /// Walks the numbered lines in document order and greedily keeps a single monotonically
    /// counting-up sequence (1, 2, 3, …), skipping any number that doesn't continue it. This is
    /// what tells a real question sequence apart from an unrelated numbered list embedded inside
    /// one question (e.g. true/false statements 1–4 sitting between question 22 and 23) — the
    /// embedded list doesn't continue the count, so it's skipped rather than breaking the
    /// sequence or being mistaken for a restart.
    /// </summary>
    private static List<Match> FindQuestionBoundaries(string text)
    {
        var allMatches = NumberedQuestion.Matches(text).Cast<Match>().ToList();

        var candidates = new List<Match>();
        for (var i = 0; i < allMatches.Count; i++)
        {
            var blockEnd = i + 1 < allMatches.Count ? allMatches[i + 1].Index : text.Length;
            if (blockEnd - allMatches[i].Index >= MinBlockLength)
            {
                candidates.Add(allMatches[i]);
            }
        }

        var boundaries = new List<Match>();
        var expectedNext = -1;

        foreach (var match in candidates)
        {
            if (!int.TryParse(match.Groups["num"].Value, out var number)) continue;

            if (boundaries.Count == 0 || number == expectedNext)
            {
                boundaries.Add(match);
                expectedNext = number + 1;
            }
        }

        return boundaries;
    }

    private static Question ToQuestion(string block, int order)
    {
        var pointsMatch = PointsHint.Match(block);
        return new Question
        {
            Order = order,
            OriginalText = block,
            Type = QuestionType.OpenText,
            Points = pointsMatch.Success ? int.Parse(pointsMatch.Groups["points"].Value) : 0
        };
    }
}
