using AdaptAula.Domain;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace AdaptAula.Infrastructure.Export;

/// <summary>RENDER step (spec §7) for DOCX, built with DocumentFormat.OpenXml (free, MIT) —
/// no dependency on Word/LibreOffice being installed.</summary>
public class DocxExporter
{
    // Content width available inside an A4 page with this document's margins, at the 96 DPI Word
    // assumes for pixel-sized images — keeps embedded photos from overflowing the page.
    private const int MaxImageWidthPx = 550;
    private const int MaxImageHeightPx = 700;
    private const long EmuPerPixelAt96Dpi = 9525; // 914400 EMU/inch ÷ 96 px/inch

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
            var nextImageId = 1;

            body.AppendChild(Heading($"{assessment.Title}", "32"));
            body.AppendChild(Paragraph(BuildSubtitle(assessment), "20", italic: true));
            body.AppendChild(EmptyParagraph());

            var orderedSections = assessment.Sections.OrderBy(s => s.Order).ToList();
            var byQuestionId = adaptedQuestions.ToDictionary(a => a.QuestionId);

            foreach (var section in orderedSections)
            {
                var orderedQuestions = section.Questions
                    .OrderBy(q => q.Order)
                    .Where(q => byQuestionId.ContainsKey(q.Id))
                    .ToList();
                if (orderedQuestions.Count == 0) continue;

                if (!string.IsNullOrWhiteSpace(section.StimulusText))
                {
                    body.AppendChild(Paragraph("Enunciado / texto de referencia", "18", italic: true));
                    foreach (var block in ContentBlocks.FromInterleavedText(section.StimulusText, section.AssetRefs))
                        AppendBlock(body, mainPart, block, fontSize, justification, ref nextImageId);
                    body.AppendChild(EmptyParagraph());
                }

                foreach (var question in orderedQuestions)
                {
                    var adapted = byQuestionId[question.Id];

                    body.AppendChild(Heading($"Pregunta {question.Order + 1} · {adapted.Points} puntos", "24"));

                    foreach (var line in adapted.AdaptedText.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                        body.AppendChild(Paragraph(line.Trim(), fontSize, justification: justification));

                    foreach (var image in ContentBlocks.FromImageGallery(question.AssetRefs).OfType<ImageBlock>())
                        body.AppendChild(ImageParagraph(mainPart, image, ref nextImageId));

                    if (adapted.Supports.Count > 0)
                    {
                        foreach (var support in adapted.Supports)
                            body.AppendChild(Paragraph((style.ShowSupportsAsChecklist ? "□ " : "• ") + support, fontSize));
                    }

                    body.AppendChild(EmptyParagraph());
                }
            }

            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static void AppendBlock(
        Body body, MainDocumentPart mainPart, ContentBlock block, string fontSize, JustificationValues justification, ref int nextImageId)
    {
        switch (block)
        {
            case TextBlock text:
                body.AppendChild(Paragraph(text.Text, fontSize, justification: justification));
                break;
            case ImageBlock image:
                body.AppendChild(ImageParagraph(mainPart, image, ref nextImageId));
                break;
        }
    }

    private static Paragraph ImageParagraph(MainDocumentPart mainPart, ImageBlock image, ref int nextImageId)
    {
        var imagePartType = image.MimeType.Contains("png", StringComparison.OrdinalIgnoreCase)
            ? ImagePartType.Png
            : ImagePartType.Jpeg;

        var imagePart = mainPart.AddImagePart(imagePartType);
        using (var imageStream = new MemoryStream(image.Bytes))
            imagePart.FeedData(imageStream);
        var relationshipId = mainPart.GetIdOfPart(imagePart);

        var (widthEmu, heightEmu) = ComputeImageSizeEmu(image.Bytes);
        var id = nextImageId++;
        var drawing = BuildImageDrawing(relationshipId, widthEmu, heightEmu, id);

        return new Paragraph(new Run(drawing));
    }

    private static (long WidthEmu, long HeightEmu) ComputeImageSizeEmu(byte[] bytes)
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

        var scale = Math.Min(1.0, Math.Min((double)MaxImageWidthPx / width, (double)MaxImageHeightPx / height));
        var finalWidthPx = Math.Max(1, (int)(width * scale));
        var finalHeightPx = Math.Max(1, (int)(height * scale));

        return ((long)finalWidthPx * EmuPerPixelAt96Dpi, (long)finalHeightPx * EmuPerPixelAt96Dpi);
    }

    /// <summary>Standard OpenXml SDK inline-picture boilerplate: an inline drawing that embeds the
    /// image part via <paramref name="relationshipId"/>, sized to <paramref name="widthEmu"/> x
    /// <paramref name="heightEmu"/> English Metric Units.</summary>
    private static Drawing BuildImageDrawing(string relationshipId, long widthEmu, long heightEmu, int id)
    {
        var name = $"Imagen {id}";
        return new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = (UInt32Value)(uint)id, Name = name },
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
