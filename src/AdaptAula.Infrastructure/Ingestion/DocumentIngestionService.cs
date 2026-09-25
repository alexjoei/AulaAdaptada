using System.Text.RegularExpressions;
using AdaptAula.Domain;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;

namespace AdaptAula.Infrastructure.Ingestion;

public class IngestionResult
{
    public required Assessment Assessment { get; init; }

    /// <summary>0-1 heuristic confidence in the extraction/split. Below 0.6 the API flags
    /// LOW_EXTRACTION_CONFIDENCE and asks the teacher to compare with the original (spec §11).</summary>
    public required double ExtractionConfidence { get; init; }
}

/// <summary>
/// INGEST step (spec §7). MVP1 scope: pasted text, DOCX and text-layer PDF. Scanned/image PDFs
/// are out of scope — they fall through to the low-confidence single-blob path rather than
/// attempting OCR (see plan's MVP1 scope cut).
/// </summary>
public class DocumentIngestionService
{
    public IngestionResult IngestPlainText(string title, string text)
    {
        var (questions, confidence) = QuestionSplitter.Split(text);
        return BuildResult(title, null, questions, confidence);
    }

    public IngestionResult IngestDocx(string fileName, Stream stream)
    {
        var text = ExtractDocxText(stream);
        var (questions, confidence) = QuestionSplitter.Split(text);
        return BuildResult(fileName, fileName, questions, confidence);
    }

    public IngestionResult IngestPdf(string fileName, Stream stream)
    {
        var (text, perPageDensity) = ExtractPdfText(stream);
        var (questions, splitConfidence) = QuestionSplitter.Split(text);

        // A text-layer PDF has plenty of characters per page; a scanned one has almost none.
        var extractionConfidence = Math.Min(splitConfidence, perPageDensity < 40 ? 0.2 : 1.0);
        return BuildResult(fileName, fileName, questions, extractionConfidence);
    }

    private static IngestionResult BuildResult(string title, string? sourceFileName, List<Question> questions, double confidence)
    {
        var section = new Section { Title = "General", Order = 0, Questions = questions };
        var assessment = new Assessment
        {
            Title = title,
            SourceFileName = sourceFileName,
            TotalPoints = questions.Sum(q => q.Points),
            Sections = new List<Section> { section }
        };
        section.AssessmentId = assessment.Id;
        foreach (var q in questions) q.SectionId = section.Id;

        return new IngestionResult { Assessment = assessment, ExtractionConfidence = confidence };
    }

    private static string ExtractDocxText(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document.Body;
        if (body is null) return string.Empty;

        var paragraphs = body.Elements<Paragraph>()
            .Select(p => p.InnerText)
            .ToList();

        // Blank paragraphs become blank lines so QuestionSplitter's paragraph-fallback still works.
        return string.Join("\n\n", CollapseConsecutiveBlanks(paragraphs));
    }

    private static IEnumerable<string> CollapseConsecutiveBlanks(List<string> paragraphs)
    {
        var lastWasBlank = false;
        foreach (var p in paragraphs)
        {
            var isBlank = string.IsNullOrWhiteSpace(p);
            if (isBlank && lastWasBlank) continue;
            yield return p;
            lastWasBlank = isBlank;
        }
    }

    // Codes like "D4PI2304CE01" are internal item identifiers printed in a thin margin strip next
    // to the real question number (seen in official INEE-style assessments). They carry no
    // meaning for a student and, once word order is reconstructed, tend to land right next to the
    // number they're printed beside — so they get stripped outright rather than confusing the
    // question splitter.
    private static readonly Regex ItemCodeToken = new(@"\bD4P[Ii]\d{3,4}[A-Za-z]{2}\d{1,2}\b", RegexOptions.Compiled);
    private static readonly Regex PageFooterNumberLine = new(@"(?m)^[ \t]*\d{1,3}[ \t]*$", RegexOptions.Compiled);

    private static (string Text, double AvgCharsPerPage) ExtractPdfText(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);
        var pageLines = pdf.GetPages().Select(ReconstructLines).ToList();

        RemoveRepeatedHeadersAndFooters(pageLines);

        var pageTexts = pageLines
            .Select(lines => StripNoise(string.Join("\n", lines)))
            .ToList();
        var avgChars = pageTexts.Count == 0 ? 0 : pageTexts.Average(t => t.Length);
        return (string.Join("\n\n", pageTexts), avgChars);
    }

    /// <summary>
    /// PdfPig's raw <c>Page.Text</c> follows the order glyphs were written into the PDF's content
    /// stream, which for layouts with a decorative margin (big stylized question numbers, tiny
    /// rotated item codes, icon captions) can interleave wildly with the actual paragraph text.
    /// Reconstructing lines from word positions — grouping words that share a baseline, then
    /// ordering those lines top-to-bottom and left-to-right — recovers plain reading order for the
    /// common case of a single reading column, which is what the question splitter needs.
    /// </summary>
    private static List<string> ReconstructLines(Page page)
    {
        var words = page.GetWords(NearestNeighbourWordExtractor.Instance)
            .Where(w => !string.IsNullOrWhiteSpace(w.Text))
            .ToList();
        if (words.Count == 0) return new List<string>();

        const double lineTolerance = 3.0;
        var lines = new List<List<Word>>();
        foreach (var word in words.OrderByDescending(w => w.BoundingBox.Bottom))
        {
            var line = lines.FirstOrDefault(l => Math.Abs(l.Average(w => w.BoundingBox.Bottom) - word.BoundingBox.Bottom) <= lineTolerance);
            if (line is null)
            {
                line = new List<Word>();
                lines.Add(line);
            }
            line.Add(word);
        }

        return lines
            .OrderByDescending(l => l.Average(w => w.BoundingBox.Bottom))
            .Select(l => string.Join(" ", l.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)))
            .ToList();
    }

    /// <summary>Running headers/footers repeat verbatim on most pages; strip them so they don't
    /// get glued onto the start of whatever question happens to follow them on the page.</summary>
    private static void RemoveRepeatedHeadersAndFooters(List<List<string>> pageLines)
    {
        if (pageLines.Count < 3) return;

        var frequency = pageLines
            .SelectMany(lines => lines.Select(l => l.Trim()).Distinct())
            .Where(l => l.Length is > 0 and < 150)
            .GroupBy(l => l)
            .ToDictionary(g => g.Key, g => g.Count());

        var threshold = Math.Max(3, pageLines.Count / 2);
        foreach (var lines in pageLines)
        {
            lines.RemoveAll(l => frequency.TryGetValue(l.Trim(), out var count) && count >= threshold);
        }
    }

    private static string StripNoise(string pageText)
    {
        var cleaned = ItemCodeToken.Replace(pageText, "");
        cleaned = PageFooterNumberLine.Replace(cleaned, "");
        var lines = cleaned.Split('\n').Select(l => Regex.Replace(l, @"[ \t]{2,}", " ").TrimEnd());
        return string.Join("\n", lines);
    }
}
