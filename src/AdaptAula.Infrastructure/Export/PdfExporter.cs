using AdaptAula.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AdaptAula.Infrastructure.Export;

/// <summary>RENDER step (spec §7) for PDF, via QuestPDF (Community license — free for this
/// project). Generates directly from the adapted model, no headless Word/LibreOffice needed.</summary>
public class PdfExporter
{
    public byte[] Export(
        Assessment assessment,
        AdaptationPlan plan,
        IReadOnlyDictionary<Guid, Question> questionsById,
        IReadOnlyList<AdaptedQuestion> adaptedQuestions)
    {
        var style = ExportStyle.From(plan);
        var bodySize = style.LargeAccessibleFont ? 14 : 11;
        var lineHeight = style.LargeAccessibleFont ? 1.5f : 1.2f;
        var leftAlign = style.LeftAlignLowDensity;

        var orderedSections = assessment.Sections.OrderBy(s => s.Order).ToList();
        var byQuestionId = adaptedQuestions.ToDictionary(a => a.QuestionId);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(36);
                page.DefaultTextStyle(t => t.FontSize(bodySize).LineHeight(lineHeight));

                page.Header().Column(col =>
                {
                    col.Item().Text(assessment.Title).FontSize(bodySize + 8).Bold();
                    col.Item().Text(BuildSubtitle(assessment)).FontSize(bodySize).Italic();
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    foreach (var section in orderedSections)
                    {
                        var orderedQuestions = section.Questions
                            .OrderBy(q => q.Order)
                            .Where(q => byQuestionId.ContainsKey(q.Id))
                            .ToList();
                        if (orderedQuestions.Count == 0) continue;

                        if (!string.IsNullOrWhiteSpace(section.StimulusText))
                            col.Item().PaddingTop(14).Element(e => RenderStimulus(e, section.StimulusText, section.AssetRefs, bodySize));

                        foreach (var question in orderedQuestions)
                        {
                            var adapted = byQuestionId[question.Id];
                            col.Item().PaddingTop(14).Element(e => RenderQuestion(e, question, adapted, style, bodySize, leftAlign));
                        }
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Aula Adaptada").FontSize(9);
                    t.Span(" · versión adaptada, uso docente").FontSize(9);
                });
            });
        });

        return document.GeneratePdf();
    }

    /// <summary>Grade/subject/points, joined loosely — grade and subject are optional (a teacher may
    /// not have specified them), so only the parts that are actually present appear.</summary>
    private static string BuildSubtitle(Assessment assessment)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(assessment.Subject)) parts.Add(assessment.Subject);
        if (assessment.Grade is not null) parts.Add($"{assessment.Grade}º");
        parts.Add($"{assessment.TotalPoints} puntos");
        return string.Join(" · ", parts);
    }

    /// <summary>The shared reading passage/instructions a group of questions refers to — rendered
    /// once above them, in a visually distinct panel, with its images interleaved inline in the
    /// same reading order as the original document (mirrors the web app's Analysis screen).</summary>
    private static void RenderStimulus(QuestPDF.Infrastructure.IContainer container, string stimulusText, List<string> assetRefs, int bodySize)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Column(col =>
        {
            col.Item().Text("Enunciado / texto de referencia").FontSize(bodySize - 1).Italic().FontColor(Colors.Grey.Darken1);

            foreach (var block in ContentBlocks.FromInterleavedText(stimulusText, assetRefs))
            {
                switch (block)
                {
                    case TextBlock text:
                        col.Item().PaddingTop(6).Text(text.Text);
                        break;
                    case ImageBlock image:
                        col.Item().PaddingTop(6).MaxHeight(240).Image(image.Bytes).FitArea();
                        break;
                }
            }
        });
    }

    private static void RenderQuestion(
        QuestPDF.Infrastructure.IContainer container, Question question, AdaptedQuestion adapted,
        ExportStyle style, int bodySize, bool leftAlign)
    {
        container.Border(1).BorderColor(Colors.Grey.Lighten2).Padding(10).Column(col =>
        {
            col.Item().Text($"Pregunta {question.Order + 1} · {adapted.Points} puntos").Bold().FontSize(bodySize + 1);

            foreach (var line in adapted.AdaptedText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var text = col.Item().PaddingTop(4).Text(line.Trim());
                if (leftAlign) text.AlignLeft(); else text.Justify();
            }

            foreach (var image in ContentBlocks.FromImageGallery(question.AssetRefs).OfType<ImageBlock>())
                col.Item().PaddingTop(6).MaxHeight(200).Image(image.Bytes).FitArea();

            if (adapted.Supports.Count > 0)
            {
                col.Item().PaddingTop(6).Column(supportCol =>
                {
                    foreach (var support in adapted.Supports)
                        supportCol.Item().Text((style.ShowSupportsAsChecklist ? "□ " : "• ") + support);
                });
            }
        });
    }
}
