using AdaptAula.Domain;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using UglyToad.PdfPig;

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

    private static (string Text, double AvgCharsPerPage) ExtractPdfText(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);
        var pageTexts = pdf.GetPages().Select(p => p.Text).ToList();
        var avgChars = pageTexts.Count == 0 ? 0 : pageTexts.Average(t => t.Length);
        return (string.Join("\n\n", pageTexts), avgChars);
    }
}
