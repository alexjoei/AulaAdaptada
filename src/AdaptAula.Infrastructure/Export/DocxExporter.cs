using AdaptAula.Domain;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace AdaptAula.Infrastructure.Export;

/// <summary>RENDER step (spec §7) for DOCX, built with DocumentFormat.OpenXml (free, MIT) —
/// no dependency on Word/LibreOffice being installed.</summary>
public class DocxExporter
{
    public byte[] Export(
        Assessment assessment,
        AdaptationPlan plan,
        IReadOnlyDictionary<Guid, Question> questionsById,
        IReadOnlyList<AdaptedQuestion> adaptedQuestions)
    {
        var style = ExportStyle.From(plan);
        var fontSize = style.LargeAccessibleFont ? "28" : "22"; // half-points: 14pt / 11pt
        var justification = style.LeftAlignLowDensity ? JustificationValues.Left : JustificationValues.Both;

        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            body.AppendChild(Heading($"{assessment.Title}", "32"));
            body.AppendChild(Paragraph($"{assessment.Subject} · {assessment.Grade}º · {assessment.TotalPoints} puntos", "20", italic: true));
            body.AppendChild(EmptyParagraph());

            var orderedQuestions = questionsById.Values.OrderBy(q => q.Order).ToList();
            var byQuestionId = adaptedQuestions.ToDictionary(a => a.QuestionId);

            foreach (var question in orderedQuestions)
            {
                if (!byQuestionId.TryGetValue(question.Id, out var adapted)) continue;

                body.AppendChild(Heading($"Pregunta {question.Order + 1} · {adapted.Points} puntos", "24"));

                foreach (var line in adapted.AdaptedText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    body.AppendChild(Paragraph(line.Trim(), fontSize, justification: justification));

                if (adapted.Supports.Count > 0)
                {
                    foreach (var support in adapted.Supports)
                        body.AppendChild(Paragraph((style.ShowSupportsAsChecklist ? "□ " : "• ") + support, fontSize));
                }

                body.AppendChild(EmptyParagraph());
            }

            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static Paragraph Heading(string text, string fontSize) =>
        Paragraph(text, fontSize, bold: true);

    private static Paragraph EmptyParagraph() => new(new Run(new Text(string.Empty)));

    private static Paragraph Paragraph(string text, string fontSize, bool bold = false, bool italic = false,
        JustificationValues? justification = null)
    {
        var runProperties = new RunProperties(
            new RunFonts { Ascii = "Arial" },
            new FontSize { Val = fontSize });
        if (bold) runProperties.AppendChild(new Bold());
        if (italic) runProperties.AppendChild(new Italic());

        var paragraphProperties = new ParagraphProperties(
            new Justification { Val = justification ?? JustificationValues.Both },
            new SpacingBetweenLines { Line = "360", LineRule = LineSpacingRuleValues.Auto });

        return new Paragraph(paragraphProperties, new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
    }
}
