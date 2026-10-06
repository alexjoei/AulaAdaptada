using AdaptAula.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace AdaptAula.Infrastructure.Export;

/// <summary>RENDER step (spec §7) for PDF, via QuestPDF (Community license — free for this project). Draws the
/// same document model as <see cref="DocxExporter"/>, so both files match what the teacher edited (V2 §9, §20).</summary>
public class PdfExporter
{
    private const float LineHeightPt = 26f;
    private const int GridColumns = 26;

    public byte[] Render(IReadOnlyList<DocBlock> blocks, DocumentStyle style, string footer = "Aula Adaptada · versión adaptada, uso docente")
    {
        try
        {
            return Build(blocks, style, footer, style.FontFamily).GeneratePdf();
        }
        catch (Exception ex) when (ex.Message.Contains("font", StringComparison.OrdinalIgnoreCase) ||
                                    ex.Message.Contains("glyph", StringComparison.OrdinalIgnoreCase) ||
                                    ex.Message.Contains("typeface", StringComparison.OrdinalIgnoreCase))
        {
            // The requested family isn't installed on this machine: fall back to the library's default face rather
            // than failing the export.
            return Build(blocks, style, footer, null).GeneratePdf();
        }
    }

    private static Document Build(IReadOnlyList<DocBlock> blocks, DocumentStyle style, string footer, string? family) =>
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin((float)Math.Clamp(style.MarginMm, 5, 60), Unit.Millimetre);
                page.DefaultTextStyle(t =>
                {
                    var s = t.FontSize((float)style.FontSizePt).LineHeight((float)style.LineSpacing);
                    if (family is not null) s = s.FontFamily(family);
                    if (style.LetterSpacingPt > 0) s = s.LetterSpacing((float)(style.LetterSpacingPt / style.FontSizePt));
                    if (style.HighContrast) s = s.FontColor(Colors.Black);
                    return s;
                });

                page.Content().Column(col => RenderBlocks(col, blocks, style));

                page.Footer().AlignCenter().Text(t => t.Span(footer).FontSize(9).FontColor(Colors.Grey.Darken1));
            });
        });

    private static void RenderBlocks(ColumnDescriptor col, IReadOnlyList<DocBlock> blocks, DocumentStyle style)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock p:
                    RenderParagraph(col, p, style);
                    break;

                case ImageDocBlock image:
                {
                    var item = col.Item().PaddingVertical(4);
                    if (image.WidthPx is > 0) item = item.MaxWidth((float)(image.WidthPx.Value * 0.75));
                    item.MaxHeight(260).Image(image.Bytes).FitArea();
                    break;
                }

                case PageBreakBlock:
                    col.Item().PageBreak();
                    break;

                case AnswerSpaceBlock space:
                    RenderAnswerSpace(col, space);
                    break;

                case TableBlock table:
                    RenderTable(col, table, style);
                    break;

                case BoxBlock box:
                {
                    var background = box.Kind == "stimulus" ? Colors.Grey.Lighten4 : Colors.White;
                    col.Item().PaddingBottom(8).Background(background).Border(0.8f).BorderColor(Colors.Grey.Medium).Padding(8)
                        .Column(inner => RenderBlocks(inner, box.Children, style));
                    break;
                }
            }
        }
    }

    private static void RenderParagraph(ColumnDescriptor col, ParagraphBlock block, DocumentStyle style)
    {
        var sizeFactor = block.HeadingLevel switch { 1 => 1.7, 2 => 1.3, 3 => 1.15, _ => 1.0 };
        var heading = block.HeadingLevel > 0;
        var item = col.Item().PaddingBottom((float)style.ParagraphSpacingPt);
        if (heading) item = item.PaddingTop(4);
        if (block.ListKind is not null) item = item.PaddingLeft(16 * (block.ListDepth + 1));
        if (block.LineSpacing is { } ls) item = item.DefaultTextStyle(t => t.LineHeight((float)ls));

        item.Text(text =>
        {
            switch ((block.Align ?? style.Align)?.ToLowerInvariant())
            {
                case "center": text.AlignCenter(); break;
                case "right": text.AlignRight(); break;
                case "justify": text.Justify(); break;
                default: text.AlignLeft(); break;
            }

            if (block.ListKind is not null)
                text.Span(block.ListKind == "ordered" ? $"{block.ListIndex}. " : "• ");

            foreach (var run in block.Runs)
            {
                if (run.LineBreak) { text.Span("\n"); continue; }

                var size = run.Style.SizePt ?? style.FontSizePt * sizeFactor;
                var wordSpacing = run.Style.WordSpacingPt ?? style.WordSpacingPt;
                var segments = wordSpacing > 0 ? TextSegments.SplitSpaces(run.Text) : new List<(string, bool)> { (run.Text, false) };

                foreach (var (segment, isSpace) in segments)
                {
                    var span = text.Span(segment);
                    span = span.FontSize((float)size);
                    if (run.Style.Bold || heading) span = span.Bold();
                    if (run.Style.Italic) span = span.Italic();
                    if (run.Style.Underline) span = span.Underline();
                    if (run.Style.Strike) span = span.Strikethrough();
                    if (!style.HighContrast)
                    {
                        if (run.Style.Color is not null) span = span.FontColor("#" + run.Style.Color);
                        if (run.Style.Highlight is not null) span = span.BackgroundColor("#" + run.Style.Highlight);
                    }
                    if (run.Style.Font is not null) span = span.FontFamily(run.Style.Font);
                    var letter = (run.Style.LetterSpacingPt ?? 0) + (isSpace ? wordSpacing : 0);
                    if (letter > 0) span = span.LetterSpacing((float)(letter / size));
                }
            }
        });
    }

    private static void RenderAnswerSpace(ColumnDescriptor col, AnswerSpaceBlock space)
    {
        if (space.Lines <= 0) return;

        if (space.Grid)
        {
            col.Item().PaddingBottom(8).Table(table =>
            {
                table.ColumnsDefinition(c => { for (var i = 0; i < GridColumns; i++) c.RelativeColumn(); });
                for (var r = 0; r < space.Lines; r++)
                    for (var c = 0; c < GridColumns; c++)
                        table.Cell().Row((uint)(r + 1)).Column((uint)(c + 1)).Height(14).Border(0.4f).BorderColor(Colors.Grey.Lighten1);
            });
            return;
        }

        for (var i = 0; i < space.Lines; i++)
            col.Item().Height(LineHeightPt).BorderBottom(space.Ruled ? 0.8f : 0.5f).BorderColor(space.Ruled ? Colors.Grey.Darken1 : Colors.Grey.Lighten1);
        col.Item().Height(8);
    }

    private static void RenderTable(ColumnDescriptor col, TableBlock block, DocumentStyle style)
    {
        var columns = Math.Max(1, block.Rows.Max(r => r.Count));
        col.Item().PaddingBottom(8).Table(table =>
        {
            table.ColumnsDefinition(c => { for (var i = 0; i < columns; i++) c.RelativeColumn(); });
            for (var r = 0; r < block.Rows.Count; r++)
            {
                for (var c = 0; c < columns; c++)
                {
                    var content = c < block.Rows[r].Count ? block.Rows[r][c] : new List<DocBlock>();
                    var cell = table.Cell().Row((uint)(r + 1)).Column((uint)(c + 1)).Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4);
                    if (block.HeaderRow && r == 0) cell = cell.Background(Colors.Grey.Lighten3);
                    cell.Column(inner => RenderBlocks(inner, content, style));
                }
            }
        });
    }
}
