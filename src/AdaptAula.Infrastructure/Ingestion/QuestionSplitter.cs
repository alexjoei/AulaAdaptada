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

    public static (List<Question> Questions, double Confidence) Split(string text)
    {
        text = text.Replace("\r\n", "\n").Trim();
        var matches = NumberedQuestion.Matches(text);

        if (matches.Count >= 2)
        {
            var questions = new List<Question>();
            for (var i = 0; i < matches.Count; i++)
            {
                var start = matches[i].Index;
                var end = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
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
