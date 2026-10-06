using AdaptAula.Domain;
using AdaptAula.Infrastructure.Export;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Xunit;

namespace AdaptAula.Tests;

public class DocxExporterTests
{
    private static byte[] Render(string html, DocumentStyle? style = null) =>
        new DocxExporter().Render(RichDocumentParser.Parse(html), style ?? new DocumentStyle());

    [Fact]
    public void Render_EmbedsOneImagePartPerSectionAndQuestionImage()
    {
        var (assessment, plan, questionsById, adapted, _) = ExportFixtures.Build();
        var html = DocumentHtmlBuilder.BuildAdapted(assessment, plan, questionsById, adapted);

        using var stream = new MemoryStream(Render(html));
        using var document = WordprocessingDocument.Open(stream, false);

        // One from the section's shared stimulus, one from the question's own AssetRefs.
        Assert.Equal(2, document.MainDocumentPart!.ImageParts.Count());

        var bodyText = document.MainDocumentPart.Document.Body!.InnerText;
        Assert.Contains("Enunciado / texto de referencia", bodyText);
        Assert.Contains("Look at this information.", bodyText);
        Assert.Contains("Pregunta 2", bodyText);
    }

    [Fact]
    public void Render_KeepsTheTeachersInlineFormatting()
    {
        const string html = "<p>Normal <strong>negrita</strong> <u>subrayado</u> <span style=\"font-size: 20pt; color: #ff0000\">grande</span> " +
                            "<mark style=\"background-color: #ffff00\">resaltado</mark></p>";

        using var stream = new MemoryStream(Render(html));
        using var document = WordprocessingDocument.Open(stream, false);
        var runs = document.MainDocumentPart!.Document.Body!.Descendants<Run>().ToList();

        Assert.Contains(runs, r => r.InnerText == "negrita" && r.RunProperties!.Bold is not null);
        Assert.Contains(runs, r => r.InnerText == "subrayado" && r.RunProperties!.Underline is not null);
        var big = runs.Single(r => r.InnerText == "grande");
        Assert.Equal("40", big.RunProperties!.FontSize!.Val!.Value); // 20pt = 40 half-points
        Assert.Equal("FF0000", big.RunProperties.Color!.Val!.Value);
        Assert.Equal("FFFF00", runs.Single(r => r.InnerText == "resaltado").RunProperties!.GetFirstChild<Shading>()!.Fill!.Value);
    }

    [Fact]
    public void Render_AppliesTheStudentsStyle_FontSizeSpacingAndMargins()
    {
        var style = new DocumentStyle { FontFamily = "Verdana", FontSizePt = 14, LineSpacing = 1.5, MarginMm = 25, Align = "left" };

        using var stream = new MemoryStream(Render("<p>Hola mundo</p>", style));
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart!.Document.Body!;

        var run = body.Descendants<Run>().First(r => r.InnerText == "Hola mundo");
        Assert.Equal("28", run.RunProperties!.FontSize!.Val!.Value);
        Assert.Equal("Verdana", run.RunProperties.RunFonts!.Ascii!.Value);
        Assert.Equal("360", body.Descendants<SpacingBetweenLines>().First().Line!.Value); // 1.5 x 240
        Assert.InRange((int)body.GetFirstChild<SectionProperties>()!.GetFirstChild<PageMargin>()!.Left!.Value, 1400, 1430); // 25 mm ~ 1417 twips
    }

    [Fact]
    public void Render_WordSpacing_WidensTheSpacesOnly()
    {
        using var stream = new MemoryStream(Render("<p>uno dos</p>", new DocumentStyle { WordSpacingPt = 4 }));
        using var document = WordprocessingDocument.Open(stream, false);
        var runs = document.MainDocumentPart!.Document.Body!.Descendants<Run>().Where(r => r.InnerText.Length > 0).ToList();

        var space = runs.Single(r => r.InnerText == " ");
        Assert.Equal(80, space.RunProperties!.GetFirstChild<Spacing>()!.Val!.Value); // 4pt = 80 twentieths
        Assert.Null(runs.Single(r => r.InnerText == "uno").RunProperties!.GetFirstChild<Spacing>());
    }

    [Fact]
    public void Render_PageBreaksAndAnswerSpaces_AreRealStructures()
    {
        const string html = "<p>A</p><div data-type=\"page-break\"></div><p>B</p><div data-type=\"answer-space\" data-lines=\"4\" data-ruled=\"true\"></div>";

        using var stream = new MemoryStream(Render(html));
        using var document = WordprocessingDocument.Open(stream, false);
        var body = document.MainDocumentPart!.Document.Body!;

        Assert.Contains(body.Descendants<Break>(), b => b.Type?.Value == BreakValues.Page);
        Assert.Equal(4, body.Descendants<ParagraphBorders>().Count()); // one ruled line per requested line
    }

    [Fact]
    public void Render_HighContrast_ForcesBlackTextAndDropsHighlights()
    {
        const string html = "<p><span style=\"color:#888888\">gris</span> <mark>marca</mark></p>";

        using var stream = new MemoryStream(Render(html, new DocumentStyle { HighContrast = true }));
        using var document = WordprocessingDocument.Open(stream, false);
        var runs = document.MainDocumentPart!.Document.Body!.Descendants<Run>().Where(r => r.InnerText.Length > 0).ToList();

        Assert.All(runs, r => Assert.Equal("000000", r.RunProperties!.Color!.Val!.Value));
        Assert.All(runs, r => Assert.Null(r.RunProperties!.GetFirstChild<Shading>()));
    }
}
