using System.Linq;
using AdaptAula.Infrastructure.Ai;
using AdaptAula.Infrastructure.Ingestion;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace AdaptAula.Tests;

/// <summary>Test double standing in for the real Gemini call: <see cref="DocumentIngestionService"/>
/// is now a thin mapping layer over whatever <see cref="IDocumentStructureExtractor"/> returns, so
/// these tests script that response and check the mapping into the domain model, rather than
/// exercising a live/non-deterministic model call.</summary>
internal class FakeDocumentStructureExtractor : IDocumentStructureExtractor
{
    public DocumentStructure PdfResult { get; set; } = new(new List<ExtractedSection>(), 1.0);
    public DocumentStructure TextResult { get; set; } = new(new List<ExtractedSection>(), 1.0);

    public Task<DocumentStructure> ExtractFromPdfAsync(byte[] pdfBytes, CancellationToken ct = default) => Task.FromResult(PdfResult);

    public Task<DocumentStructure> ExtractFromTextAsync(string text, CancellationToken ct = default) => Task.FromResult(TextResult);
}

public class DocumentIngestionServiceTests
{
    // A minimal valid 1x1 PNG, solid-colored — QuestPDF stretches it to whatever container size is
    // given. Only usable for the tiny-size filter test below: a real content photo has to have
    // actual color variation, or DocumentIngestionService's flat-color-fill filter (correctly)
    // excludes it as decorative, same as it would a real solid-color background panel.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    // Stands in for "a real content photo" in tests — enough color variation to survive the
    // flat-color-fill filter, the same way an actual photograph would.
    private static readonly byte[] VariedPng = BuildVariedPng();

    private static byte[] BuildVariedPng()
    {
        using var image = new Image<Rgb24>(60, 60);
        for (var y = 0; y < image.Height; y++)
            for (var x = 0; x < image.Width; x++)
                image[x, y] = new Rgb24((byte)(x * 4), (byte)(y * 4), (byte)((x + y) * 2));
        using var ms = new MemoryStream();
        image.SaveAsPng(ms);
        return ms.ToArray();
    }

    static DocumentIngestionServiceTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // All fixtures below are a single page, so a section with StartPage=1, EndPage=1 covers it.
    private static byte[] BuildPdfWithImages(byte[] imageBytes, params (double Width, double Height)[] images)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A5);
                page.Margin(20);
                page.Content().Column(col =>
                {
                    col.Item().Text("Some page content so the PDF isn't empty.");
                    foreach (var (width, height) in images)
                        col.Item().Width((float)width).Height((float)height).Image(imageBytes);
                });
            });
        }).GeneratePdf();
    }

    [Fact]
    public async Task IngestPlainTextAsync_ComposesOptionsIntoDisplayedText_AndKeepsThemStructured()
    {
        var fake = new FakeDocumentStructureExtractor
        {
            TextResult = new DocumentStructure(
                new List<ExtractedSection>
                {
                    new(null, new List<int>(), new List<ExtractedQuestion>
                    {
                        new("What day of the week is it?", new List<string> { "Friday", "Sunday" }, 3, new List<int>())
                    })
                },
                0.9)
        };

        var result = await new DocumentIngestionService(fake).IngestPlainTextAsync("Test", "irrelevant raw text");

        var question = Assert.Single(result.Assessment.Sections.SelectMany(s => s.Questions));
        Assert.Equal("What day of the week is it?\nA. Friday\nB. Sunday", question.OriginalText);
        Assert.Equal(new List<string> { "Friday", "Sunday" }, question.Options);
        Assert.Equal(3, question.Points);
        Assert.Equal(0.9, result.ExtractionConfidence);
    }

    [Fact]
    public async Task IngestPlainTextAsync_LeavesTextUnchangedAndPointsAtZero_WhenNoOptionsOrPointsHint()
    {
        var fake = new FakeDocumentStructureExtractor
        {
            TextResult = new DocumentStructure(
                new List<ExtractedSection>
                {
                    new(null, new List<int>(), new List<ExtractedQuestion>
                    {
                        new("Explain photosynthesis in your own words.", new List<string>(), null, new List<int>())
                    })
                },
                0.5)
        };

        var result = await new DocumentIngestionService(fake).IngestPlainTextAsync("Test", "text");

        var question = Assert.Single(result.Assessment.Sections.SelectMany(s => s.Questions));
        Assert.Equal("Explain photosynthesis in your own words.", question.OriginalText);
        Assert.Empty(question.Options);
        Assert.Equal(0, question.Points);
    }

    [Fact]
    public async Task IngestPlainTextAsync_NumbersQuestionsGloballyAcrossSections_AndSumsPoints()
    {
        var fake = new FakeDocumentStructureExtractor
        {
            TextResult = new DocumentStructure(
                new List<ExtractedSection>
                {
                    new("Shared passage", new List<int>(), new List<ExtractedQuestion>
                    {
                        new("Q1", new List<string>(), 2, new List<int>()),
                        new("Q2", new List<string>(), 3, new List<int>())
                    }),
                    new(null, new List<int>(), new List<ExtractedQuestion> { new("Q3", new List<string>(), 5, new List<int>()) })
                },
                0.8)
        };

        var result = await new DocumentIngestionService(fake).IngestPlainTextAsync("Test", "text");

        var sections = result.Assessment.Sections.OrderBy(s => s.Order).ToList();
        Assert.Equal(2, sections.Count);
        Assert.Equal("Grupo 1", sections[0].Title);
        Assert.Equal("Grupo 2", sections[1].Title);
        Assert.Equal(new[] { 0, 1 }, sections[0].Questions.OrderBy(q => q.Order).Select(q => q.Order));
        Assert.Equal(new[] { 2 }, sections[1].Questions.Select(q => q.Order));
        Assert.Equal(10, result.Assessment.TotalPoints);
    }

    [Fact]
    public async Task IngestPdfAsync_ReplacesAnInlineImagePlaceholder_WithAResolvedMarkerAtThatPosition()
    {
        // The extractor is asked to mark exactly where an image occurs in the reading flow with a
        // literal "[IMG]" placeholder — this must land as an inline reference to the section's own
        // AssetRefs, interleaved with the surrounding text, rather than the image being shown
        // separately from the passage it illustrates.
        var pdfBytes = BuildPdfWithImages(VariedPng, (150, 120));

        var fake = new FakeDocumentStructureExtractor
        {
            PdfResult = new DocumentStructure(
                new List<ExtractedSection>
                {
                    new("First paragraph.\n[IMG]\nSecond paragraph.", new List<int> { 0 }, new List<ExtractedQuestion>(), StartPage: 1, EndPage: 1)
                },
                0.9)
        };

        using var stream = new MemoryStream(pdfBytes);
        var result = await new DocumentIngestionService(fake).IngestPdfAsync("test.pdf", stream);

        var section = Assert.Single(result.Assessment.Sections);
        var image = Assert.Single(section.AssetRefs);
        Assert.DoesNotContain("[IMG]", section.StimulusText);
        Assert.Contains("First paragraph.", section.StimulusText);
        Assert.Contains("Second paragraph.", section.StimulusText);
        // The marker must reference this section's own AssetRefs by position (index 0 here — the
        // only image), so the Analysis screen can resolve it to an actual <img> at render time.
        Assert.Contains("IMG:0", section.StimulusText);
        Assert.True(
            section.StimulusText!.IndexOf("First paragraph.") < section.StimulusText.IndexOf("IMG:0") &&
            section.StimulusText.IndexOf("IMG:0") < section.StimulusText.IndexOf("Second paragraph."),
            "the marker should sit between the two paragraphs, matching where [IMG] was in the source text");
    }

    [Fact]
    public async Task IngestPdfAsync_AppendsAPageMatchedImage_WhenTheAiNeverPlacedAMarkerForIt()
    {
        // An image page-matched to a section that never got an [IMG] placeholder for it (the model
        // missed marking it) must still show up — appended to the passage rather than silently lost.
        var pdfBytes = BuildPdfWithImages(VariedPng, (150, 120));

        var fake = new FakeDocumentStructureExtractor
        {
            PdfResult = new DocumentStructure(
                new List<ExtractedSection>
                {
                    new("A passage with no inline marker.", new List<int>(), new List<ExtractedQuestion>(), StartPage: 1, EndPage: 1)
                },
                0.9)
        };

        using var stream = new MemoryStream(pdfBytes);
        var result = await new DocumentIngestionService(fake).IngestPdfAsync("test.pdf", stream);

        var section = Assert.Single(result.Assessment.Sections);
        Assert.Single(section.AssetRefs);
        Assert.Contains("A passage with no inline marker.", section.StimulusText);
        Assert.Contains("IMG:0", section.StimulusText);
        Assert.Empty(result.Assessment.ImageDataUris);
    }

    [Fact]
    public async Task IngestPdfAsync_ResolvesPreAssignedImageRefsToDataUris_ForSectionAndQuestion()
    {
        // StartPage/EndPage default to 0 here deliberately, so AssignImagesByPage's own page-matching
        // never fires — this test is only about the resolve-refs-to-data-URIs mapping, independent of
        // how those refs got populated.
        var pdfBytes = BuildPdfWithImages(VariedPng, (150, 120), (150, 120)); // two candidate images, indices 0 and 1

        var fake = new FakeDocumentStructureExtractor
        {
            PdfResult = new DocumentStructure(
                new List<ExtractedSection>
                {
                    new("A shared passage", new List<int> { 0 }, new List<ExtractedQuestion>
                    {
                        new("A question about it", new List<string>(), null, new List<int> { 1 })
                    })
                },
                0.85)
        };

        using var stream = new MemoryStream(pdfBytes);
        var result = await new DocumentIngestionService(fake).IngestPdfAsync("test.pdf", stream);

        var section = Assert.Single(result.Assessment.Sections);
        var sectionImage = Assert.Single(section.AssetRefs);
        Assert.StartsWith("data:image/", sectionImage);

        var question = Assert.Single(section.Questions);
        var questionImage = Assert.Single(question.AssetRefs);
        Assert.StartsWith("data:image/", questionImage);
    }

    [Fact]
    public async Task IngestPdfAsync_LeavesAnUnreferencedCandidateImage_InTheFallbackList()
    {
        var pdfBytes = BuildPdfWithImages(VariedPng, (150, 120), (150, 120)); // index 1 will never be referenced below

        var fake = new FakeDocumentStructureExtractor
        {
            PdfResult = new DocumentStructure(
                new List<ExtractedSection>
                {
                    new("A shared passage", new List<int> { 0 }, new List<ExtractedQuestion>())
                },
                0.85)
        };

        using var stream = new MemoryStream(pdfBytes);
        var result = await new DocumentIngestionService(fake).IngestPdfAsync("test.pdf", stream);

        Assert.Single(result.Assessment.ImageDataUris);
    }

    [Fact]
    public async Task IngestPdfAsync_IgnoresAnOutOfRangeImageRef_RatherThanThrowing()
    {
        var pdfBytes = BuildPdfWithImages(VariedPng, (150, 120));

        var fake = new FakeDocumentStructureExtractor
        {
            PdfResult = new DocumentStructure(
                new List<ExtractedSection>
                {
                    new(null, new List<int>(), new List<ExtractedQuestion>
                    {
                        new("A question", new List<string>(), null, new List<int> { 7 }) // no such candidate image
                    })
                },
                0.85)
        };

        using var stream = new MemoryStream(pdfBytes);
        var result = await new DocumentIngestionService(fake).IngestPdfAsync("test.pdf", stream);

        var question = Assert.Single(result.Assessment.Sections.SelectMany(s => s.Questions));
        Assert.Empty(question.AssetRefs);
        Assert.Single(result.Assessment.ImageDataUris); // the real image, never claimed, still surfaces
    }

    [Fact]
    public async Task IngestPdfAsync_AssignsAnImageToTheSectionCoveringItsPage()
    {
        // Images are matched to a section purely by page number (see AssignImagesByPage) — not by
        // asking the AI to visually compare them, which proved unreliable in practice.
        var pdfBytes = BuildPdfWithImages(VariedPng, (150, 120));

        var fake = new FakeDocumentStructureExtractor
        {
            PdfResult = new DocumentStructure(
                new List<ExtractedSection> { new("Passage on page 1", new List<int>(), new List<ExtractedQuestion>(), StartPage: 1, EndPage: 1) },
                0.9)
        };

        using var stream = new MemoryStream(pdfBytes);
        var result = await new DocumentIngestionService(fake).IngestPdfAsync("test.pdf", stream);

        var section = Assert.Single(result.Assessment.Sections);
        Assert.Single(section.AssetRefs);
        Assert.Empty(result.Assessment.ImageDataUris);
    }

    [Fact]
    public async Task IngestPdfAsync_LeavesAnImageUnclaimed_WhenNoSectionCoversItsPage()
    {
        var pdfBytes = BuildPdfWithImages(VariedPng, (150, 120)); // this fixture only has page 1

        var fake = new FakeDocumentStructureExtractor
        {
            PdfResult = new DocumentStructure(
                new List<ExtractedSection> { new("Passage elsewhere", new List<int>(), new List<ExtractedQuestion>(), StartPage: 5, EndPage: 5) },
                0.9)
        };

        using var stream = new MemoryStream(pdfBytes);
        var result = await new DocumentIngestionService(fake).IngestPdfAsync("test.pdf", stream);

        var section = Assert.Single(result.Assessment.Sections);
        Assert.Empty(section.AssetRefs);
        Assert.Single(result.Assessment.ImageDataUris);
    }

    [Fact]
    public async Task IngestPdfAsync_SkipsTinyDecorativeImages()
    {
        var pdfBytes = BuildPdfWithImages(TinyPng, (10, 10));
        var fake = new FakeDocumentStructureExtractor
        {
            PdfResult = new DocumentStructure(
                new List<ExtractedSection> { new(null, new List<int>(), new List<ExtractedQuestion>(), StartPage: 1, EndPage: 1) }, 1.0)
        };

        using var stream = new MemoryStream(pdfBytes);
        var result = await new DocumentIngestionService(fake).IngestPdfAsync("icons.pdf", stream);

        Assert.Empty(result.Assessment.Sections.Single().AssetRefs);
        Assert.Empty(result.Assessment.ImageDataUris);
    }

    [Fact]
    public async Task IngestPdfAsync_FiltersOutRepeatingDecorativeImages()
    {
        // Some layouts embed the exact same graphic many times (a per-question number badge, a
        // repeating banner icon) — a real content photo never legitimately repeats byte-for-byte
        // like that, so an image appearing 3+ times across the document is excluded entirely rather
        // than being wrongly attached to a section as content.
        var pdfBytes = BuildPdfWithImages(VariedPng, (150, 120), (150, 120), (150, 120));
        var fake = new FakeDocumentStructureExtractor
        {
            PdfResult = new DocumentStructure(
                new List<ExtractedSection> { new(null, new List<int>(), new List<ExtractedQuestion>(), StartPage: 1, EndPage: 1) }, 1.0)
        };

        using var stream = new MemoryStream(pdfBytes);
        var result = await new DocumentIngestionService(fake).IngestPdfAsync("badges.pdf", stream);

        Assert.Empty(result.Assessment.Sections.Single().AssetRefs);
        Assert.Empty(result.Assessment.ImageDataUris);
    }

    [Fact]
    public async Task IngestPdfAsync_ExtractsEmbeddedImages_LargeEnoughToBeMeaningful()
    {
        var pdfBytes = BuildPdfWithImages(VariedPng, (150, 120));
        var fake = new FakeDocumentStructureExtractor
        {
            PdfResult = new DocumentStructure(
                new List<ExtractedSection> { new(null, new List<int>(), new List<ExtractedQuestion>(), StartPage: 1, EndPage: 1) }, 1.0)
        };

        using var stream = new MemoryStream(pdfBytes);
        var result = await new DocumentIngestionService(fake).IngestPdfAsync("photo.pdf", stream);

        var image = Assert.Single(result.Assessment.Sections.Single().AssetRefs);
        Assert.StartsWith("data:image/", image);
    }
}
