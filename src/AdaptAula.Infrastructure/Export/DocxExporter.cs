using System.Globalization;
using AdaptAula.Domain;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace AdaptAula.Infrastructure.Export;

/// <summary>RENDER step (spec §7) for DOCX, built with DocumentFormat.OpenXml (free, MIT) — no dependency on
/// Word/LibreOffice being installed. Renders the editor's document model, so the Word file the teacher can still edit
/// matches the screen (V2 §9, §20).</summary>
public class DocxExporter
{
    // Content width inside an A4 page with default margins, at the 96 DPI Word assumes for pixel-sized images.
    private const int MaxImageWidthPx = 550;
    private const int MaxImageHeightPx = 700;
    private const long EmuPerPixelAt96Dpi = 9525; // 914400 EMU/inch ÷ 96 px/inch
    private const double TwipsPerMm = 56.6929;

    public byte[] Render(IReadOnlyList<DocBlock> blocks, DocumentStyle style)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            var context = new RenderContext(mainPart, style);

            foreach (var element in RenderBlocks(blocks, context, topLevel: true))
                body.AppendChild(element);

            var margin = (int)Math.Round(Math.Clamp(style.MarginMm, 5, 60) * TwipsPerMm);
            body.AppendChild(new SectionProperties(
                new PageSize { Width = 11906U, Height = 16838U },
                new PageMargin { Top = margin, Right = (uint)margin, Bottom = margin, Left = (uint)margin, Header = 708U, Footer = 708U, Gutter = 0U }));

            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private class RenderContext
    {
        public RenderContext(MainDocumentPart part, DocumentStyle style) { Part = part; Style = style; }
        public MainDocumentPart Part { get; }
        public DocumentStyle Style { get; }
        public int NextImageId { get; set; } = 1;
    }

    private static List<OpenXmlElement> RenderBlocks(IReadOnlyList<DocBlock> blocks, RenderContext ctx, bool topLevel)
    {
        var elements = new List<OpenXmlElement>();
        foreach (var block in blocks)
        {
            switch (block)
            {
                case ParagraphBlock p:
                    elements.Add(RenderParagraph(p, ctx));
                    break;
                case ImageDocBlock image:
                    elements.Add(RenderImage(image, ctx));
                    break;
                case PageBreakBlock:
                    elements.Add(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                    break;
                case AnswerSpaceBlock space:
                    elements.AddRange(RenderAnswerSpace(space, ctx));
                    break;
                case TableBlock table:
                    elements.Add(RenderTable(table, ctx));
                    elements.Add(Spacer());
                    break;
                case BoxBlock box:
                    elements.Add(RenderBox(box, ctx));
                    elements.Add(Spacer());
                    break;
            }
        }

        // A table cell (and the body) must not end on a table: Word needs a closing paragraph.
        if (!topLevel && (elements.Count == 0 || elements[^1] is Table)) elements.Add(Spacer());
        return elements;
    }

    private static Paragraph Spacer() =>
        new(new ParagraphProperties(new SpacingBetweenLines { After = "0", Line = "120", LineRule = LineSpacingRuleValues.Exact }), new Run(new Text(string.Empty)));

    // ---------------------------------------------------------------- text

    private static Paragraph RenderParagraph(ParagraphBlock block, RenderContext ctx)
    {
        var style = ctx.Style;
        var baseSize = style.FontSizePt;
        var sizeFactor = block.HeadingLevel switch { 1 => 1.7, 2 => 1.3, 3 => 1.15, _ => 1.0 };
        var heading = block.HeadingLevel > 0;

        var props = new ParagraphProperties();
        var lineSpacing = block.LineSpacing ?? style.LineSpacing;
        props.AppendChild(new SpacingBetweenLines
        {
            Line = ((int)Math.Round(lineSpacing * 240)).ToString(CultureInfo.InvariantCulture),
            LineRule = LineSpacingRuleValues.Auto,
            Before = heading ? "120" : "0",
            After = ((int)Math.Round(style.ParagraphSpacingPt * 20)).ToString(CultureInfo.InvariantCulture)
        });

        if (block.ListKind is not null)
        {
            var left = 360 * (block.ListDepth + 1);
            props.AppendChild(new Indentation { Left = left.ToString(), Hanging = "360" });
        }

        props.AppendChild(new Justification { Val = ToJustification(block.Align ?? style.Align) });

        var paragraph = new Paragraph(props);

        if (block.ListKind is not null)
        {
            var marker = block.ListKind == "ordered" ? $"{block.ListIndex}.\t" : "•\t";
            paragraph.AppendChild(new Run(RunProps(new RunStyle(), baseSize, style, bold: false), new Text(marker) { Space = SpaceProcessingModeValues.Preserve }));
        }

        foreach (var run in block.Runs)
        {
            var effectiveStyle = heading ? run.Style with { Bold = true } : run.Style;
            var size = (effectiveStyle.SizePt ?? baseSize * sizeFactor);
            var runProps = RunProps(effectiveStyle, size, style, bold: effectiveStyle.Bold);

            if (run.LineBreak)
            {
                paragraph.AppendChild(new Run(runProps.CloneNode(true), new Break()));
                continue;
            }

            // Word has no "word spacing" property: widen the spaces themselves with extra character spacing.
            var wordSpacing = effectiveStyle.WordSpacingPt ?? style.WordSpacingPt;
            if (wordSpacing <= 0)
            {
                paragraph.AppendChild(new Run(runProps, new Text(run.Text) { Space = SpaceProcessingModeValues.Preserve }));
                continue;
            }

            foreach (var (segment, isSpace) in TextSegments.SplitSpaces(run.Text))
            {
                var segmentProps = RunProps(effectiveStyle, size, style, bold: effectiveStyle.Bold, extraSpacingPt: isSpace ? wordSpacing : 0);
                paragraph.AppendChild(new Run(segmentProps, new Text(segment) { Space = SpaceProcessingModeValues.Preserve }));
            }
        }

        if (!paragraph.Elements<Run>().Any()) paragraph.AppendChild(new Run(new Text(string.Empty)));
        return paragraph;
    }

    /// <remarks>Child order follows the CT_RPr schema (rFonts, b, i, strike, color, spacing, sz, u, shd); Word is strict about it.</remarks>
    private static RunProperties RunProps(RunStyle run, double sizePt, DocumentStyle style, bool bold, double extraSpacingPt = 0)
    {
        var font = run.Font ?? style.FontFamily;
        var props = new RunProperties(new RunFonts { Ascii = font, HighAnsi = font, ComplexScript = font });
        if (bold) props.AppendChild(new Bold());
        if (run.Italic) props.AppendChild(new Italic());
        if (run.Strike) props.AppendChild(new Strike());

        var color = style.HighContrast ? "000000" : run.Color;
        if (color is not null) props.AppendChild(new Color { Val = color });

        var letter = (run.LetterSpacingPt ?? style.LetterSpacingPt) + extraSpacingPt;
        if (letter > 0) props.AppendChild(new Spacing { Val = (int)Math.Round(letter * 20) });

        props.AppendChild(new FontSize { Val = ((int)Math.Round(sizePt * 2)).ToString(CultureInfo.InvariantCulture) });
        if (run.Underline) props.AppendChild(new Underline { Val = UnderlineValues.Single });
        if (run.Highlight is not null && !style.HighContrast)
            props.AppendChild(new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = run.Highlight });
        return props;
    }

    private static JustificationValues ToJustification(string? align) => align?.ToLowerInvariant() switch
    {
        "center" => JustificationValues.Center,
        "right" => JustificationValues.Right,
        "justify" => JustificationValues.Both,
        _ => JustificationValues.Left
    };

    // ---------------------------------------------------------------- boxes, tables, answer space

    private static Table RenderBox(BoxBlock box, RenderContext ctx)
    {
        var fill = box.Kind == "stimulus" ? "F2F2F2" : null;
        var table = NewTable(single: true);
        var cell = new TableCell(new TableCellProperties(
            new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            new TableCellMargin(
                new TopMargin { Width = "100", Type = TableWidthUnitValues.Dxa },
                new LeftMargin { Width = "160", Type = TableWidthUnitValues.Dxa },
                new BottomMargin { Width = "100", Type = TableWidthUnitValues.Dxa },
                new RightMargin { Width = "160", Type = TableWidthUnitValues.Dxa })));
        if (fill is not null) cell.TableCellProperties!.AppendChild(new Shading { Val = ShadingPatternValues.Clear, Fill = fill });

        foreach (var element in RenderBlocks(box.Children, ctx, topLevel: false)) cell.AppendChild(element);
        table.AppendChild(new TableRow(cell));
        return table;
    }

    private static Table RenderTable(TableBlock block, RenderContext ctx)
    {
        var table = NewTable(single: false);
        var columns = Math.Max(1, block.Rows.Max(r => r.Count));
        foreach (var (row, rowIndex) in block.Rows.Select((r, i) => (r, i)))
        {
            var tableRow = new TableRow();
            for (var c = 0; c < columns; c++)
            {
                var content = c < row.Count ? row[c] : new List<DocBlock>();
                var cell = new TableCell(new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = (5000 / columns).ToString() }));
                if (block.HeaderRow && rowIndex == 0)
                    cell.TableCellProperties!.AppendChild(new Shading { Val = ShadingPatternValues.Clear, Fill = "E7E6F5" });
                foreach (var element in RenderBlocks(content, ctx, topLevel: false)) cell.AppendChild(element);
                tableRow.AppendChild(cell);
            }
            table.AppendChild(tableRow);
        }
        return table;
    }

    private static Table NewTable(bool single)
    {
        var color = single ? "808080" : "999999";
        return new Table(new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 6, Color = color },
                new LeftBorder { Val = BorderValues.Single, Size = 6, Color = color },
                new BottomBorder { Val = BorderValues.Single, Size = 6, Color = color },
                new RightBorder { Val = BorderValues.Single, Size = 6, Color = color },
                new InsideHorizontalBorder { Val = single ? BorderValues.None : BorderValues.Single, Size = 4, Color = "BBBBBB" },
                new InsideVerticalBorder { Val = single ? BorderValues.None : BorderValues.Single, Size = 4, Color = "BBBBBB" }),
            new TableLayout { Type = TableLayoutValues.Fixed }));
    }

    private static IEnumerable<OpenXmlElement> RenderAnswerSpace(AnswerSpaceBlock space, RenderContext ctx)
    {
        if (space.Lines <= 0) yield break;

        if (space.Grid)
        {
            const int cellTwips = 340;
            const int columns = 26;
            var table = new Table(new TableProperties(
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4, Color = "A0A0A0" },
                    new LeftBorder { Val = BorderValues.Single, Size = 4, Color = "A0A0A0" },
                    new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "A0A0A0" },
                    new RightBorder { Val = BorderValues.Single, Size = 4, Color = "A0A0A0" },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "C8C8C8" },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "C8C8C8" }),
                new TableLayout { Type = TableLayoutValues.Fixed }));
            for (var r = 0; r < space.Lines; r++)
            {
                var row = new TableRow(new TableRowProperties(new TableRowHeight { Val = cellTwips, HeightType = HeightRuleValues.Exact }));
                for (var c = 0; c < columns; c++)
                    row.AppendChild(new TableCell(
                        new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = cellTwips.ToString() }),
                        Spacer()));
                table.AppendChild(row);
            }
            yield return table;
            yield return Spacer();
            yield break;
        }

        for (var i = 0; i < space.Lines; i++)
        {
            yield return new Paragraph(
                new ParagraphProperties(
                    new ParagraphBorders(new BottomBorder { Val = space.Ruled ? BorderValues.Single : BorderValues.Dotted, Size = 6, Space = 1, Color = "9A9A9A" }),
                    new SpacingBetweenLines { Before = "0", After = "0", Line = "520", LineRule = LineSpacingRuleValues.Exact }),
                new Run(new Text(string.Empty)));
        }
        yield return Spacer();
    }

    // ---------------------------------------------------------------- images

    private static Paragraph RenderImage(ImageDocBlock image, RenderContext ctx)
    {
        var imagePartType = image.MimeType.Contains("png", StringComparison.OrdinalIgnoreCase)
            ? ImagePartType.Png
            : image.MimeType.Contains("gif", StringComparison.OrdinalIgnoreCase) ? ImagePartType.Gif : ImagePartType.Jpeg;

        var imagePart = ctx.Part.AddImagePart(imagePartType);
        using (var imageStream = new MemoryStream(image.Bytes))
            imagePart.FeedData(imageStream);
        var relationshipId = ctx.Part.GetIdOfPart(imagePart);

        var (widthEmu, heightEmu) = ComputeImageSizeEmu(image.Bytes, image.WidthPx);
        var id = ctx.NextImageId++;
        return new Paragraph(new Run(BuildImageDrawing(relationshipId, widthEmu, heightEmu, id, image.Alt)));
    }

    private static (long WidthEmu, long HeightEmu) ComputeImageSizeEmu(byte[] bytes, double? requestedWidthPx)
    {
        int width = MaxImageWidthPx;
        int height = MaxImageWidthPx * 3 / 4;
        try
        {
            using var stream = new MemoryStream(bytes);
            var info = SixLabors.ImageSharp.Image.Identify(stream);
            if (info is not null) { width = info.Width; height = info.Height; }
        }
        catch (SixLabors.ImageSharp.UnknownImageFormatException)
        {
            // Fall back to the default box above rather than fail the whole export over one image.
        }

        double targetWidth = requestedWidthPx is > 0 ? requestedWidthPx.Value : width;
        double targetHeight = targetWidth * height / width;
        var scale = Math.Min(1.0, Math.Min(MaxImageWidthPx / targetWidth, MaxImageHeightPx / targetHeight));
        var finalWidthPx = Math.Max(1, (int)(targetWidth * scale));
        var finalHeightPx = Math.Max(1, (int)(targetHeight * scale));

        return ((long)finalWidthPx * EmuPerPixelAt96Dpi, (long)finalHeightPx * EmuPerPixelAt96Dpi);
    }

    /// <summary>Standard OpenXml SDK inline-picture boilerplate, with the alt text screen readers announce.</summary>
    private static Drawing BuildImageDrawing(string relationshipId, long widthEmu, long heightEmu, int id, string? alt)
    {
        var name = $"Imagen {id}";
        return new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = (UInt32Value)(uint)id, Name = name, Description = alt ?? string.Empty },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(
                    new A.GraphicData(
                        new PIC.Picture(
                            new PIC.NonVisualPictureProperties(
                                new PIC.NonVisualDrawingProperties { Id = (UInt32Value)(uint)id, Name = name },
                                new PIC.NonVisualPictureDrawingProperties()),
                            new PIC.BlipFill(
                                new A.Blip { Embed = relationshipId },
                                new A.Stretch(new A.FillRectangle())),
                            new PIC.ShapeProperties(
                                new A.Transform2D(
                                    new A.Offset { X = 0L, Y = 0L },
                                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }))
                    ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
            )
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U
            });
    }
}
