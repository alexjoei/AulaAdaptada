using AdaptAula.Domain;
using AdaptAula.Infrastructure.Export;
using Xunit;

namespace AdaptAula.Tests;

public class PdfExporterTests
{
    private static byte[] Render(string html, DocumentStyle? style = null) =>
        new PdfExporter().Render(RichDocumentParser.Parse(html), style ?? new DocumentStyle());

    [Fact]
    public void Render_IncludesTheSectionStimulusAndPerQuestionImages_WithoutThrowing()
    {
        var (assessment, plan, questionsById, adapted, _) = ExportFixtures.Build();
        var withImages = Render(DocumentHtmlBuilder.BuildAdapted(assessment, plan, questionsById, adapted));

        var (a2, p2, q2, ad2, _) = ExportFixtures.Build(withImages: false);
        var withoutImages = Render(DocumentHtmlBuilder.BuildAdapted(a2, p2, q2, ad2));

        // A real assertion on layout isn't practical against raw PDF bytes, but a PDF that actually embedded two extra images is
        // measurably bigger than the same document with none — this catches a regression back to "images are silently dropped".
        Assert.True(withImages.Length > withoutImages.Length,
            $"Expected the export with embedded images ({withImages.Length} bytes) to be larger than the same export with none ({withoutImages.Length} bytes).");
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(withImages, 0, 4));
    }

    [Fact]
    public void Render_HandlesEveryBlockKindTheEditorCanProduce()
    {
        const string html = """
            <h1>Título</h1><p style="text-align:center"><strong>Centrado</strong> <em>cursiva</em> <u>sub</u>
            <span style="font-size:18pt;color:#cc0000;letter-spacing:1pt">grande</span> <mark style="background-color:#ffff00">marca</mark></p>
            <ul><li><p>uno</p></li><li><p>dos</p></li></ul><ol><li><p>primero</p></li></ol>
            <table><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></table>
            <div data-type="page-break"></div>
            <div data-type="question" data-question-id="11111111-1111-1111-1111-111111111111" data-points="2"><h2>Pregunta 1</h2><p>Texto</p>
            <div data-type="answer-space" data-lines="3" data-ruled="false" data-grid="true"></div></div>
            """;

        var bytes = Render(html, new DocumentStyle { WordSpacingPt = 3, LetterSpacingPt = 0.5, LineSpacing = 1.4 });

        Assert.True(bytes.Length > 1000);
    }

    [Fact]
    public void Render_FallsBackToTheDefaultFont_WhenTheRequestedFamilyDoesNotExist()
    {
        var bytes = Render("<p>Hola</p>", new DocumentStyle { FontFamily = "Una Fuente Que No Existe" });

        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
