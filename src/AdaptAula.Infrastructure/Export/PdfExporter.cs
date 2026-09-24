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

        var orderedQuestions = questionsById.Values.OrderBy(q => q.Order).ToList();
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
                    col.Item().Text($"{assessment.Subject} · {assessment.Grade}º · {assessment.TotalPoints} puntos")
                        .FontSize(bodySize).Italic();
                });

                page.Content().PaddingTop(12).Column(col =>
                {
                    foreach (var question in orderedQuestions)
                    {
                        if (!byQuestionId.TryGetValue(question.Id, out var adapted)) continue;

                        col.Item().PaddingTop(14).Element(e => RenderQuestion(e, question, adapted, style, bodySize, leftAlign));
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
