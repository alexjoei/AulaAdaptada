using AdaptAula.Infrastructure.Export;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace AdaptAula.Tests;

public class ContentBlocksTests
{
    // A real, ImageSharp-decodable PNG — a hand-written base64 literal risks a corrupt CRC that
    // only surfaces once something actually decodes it (as PdfExporter/DocxExporter now do).
    private static string TinyPngDataUri => $"data:image/png;base64,{Convert.ToBase64String(BuildTinyPng())}";

    private static byte[] BuildTinyPng()
    {
        using var image = new Image<Rgba32>(2, 2);
        using var stream = new MemoryStream();
        image.Save(stream, new PngEncoder());
        return stream.ToArray();
    }

    // Matches the real runtime format exactly: DocumentIngestionService.ResolvedImageMarker wraps
    // "IMG:n" in U+E000 (invisible Private Use Area) on both sides.
    private static string Marker(int index) => $"IMG:{index}";

    [Fact]
    public void FromInterleavedText_WithNoMarkers_ReturnsASingleTextBlock()
    {
        var blocks = ContentBlocks.FromInterleavedText("Just some plain stimulus text.", new List<string>());

        var block = Assert.Single(blocks);
        Assert.Equal("Just some plain stimulus text.", Assert.IsType<TextBlock>(block).Text);
    }

    [Fact]
    public void FromInterleavedText_SplitsTextAndImagesInReadingOrder()
    {
        var text = $"Look at this information.\n{Marker(0)}\nNow answer the questions below.";
        var blocks = ContentBlocks.FromInterleavedText(text, new List<string> { TinyPngDataUri });

        Assert.Equal(3, blocks.Count);
        Assert.Equal("Look at this information.", Assert.IsType<TextBlock>(blocks[0]).Text);
        Assert.IsType<ImageBlock>(blocks[1]);
        Assert.Equal("Now answer the questions below.", Assert.IsType<TextBlock>(blocks[2]).Text);
    }

    [Fact]
    public void FromInterleavedText_SkipsAnOutOfRangeMarker_ButStillAppendsTheUnreferencedRealAsset()
    {
        var blocks = ContentBlocks.FromInterleavedText($"Before\n{Marker(5)}\nAfter", new List<string> { TinyPngDataUri });

        // The marker referencing index 5 doesn't resolve to anything and is dropped, but the one
        // real asset (index 0) was never claimed by any marker, so it's still appended at the end
        // rather than silently lost.
        Assert.Equal(3, blocks.Count);
        Assert.Equal("Before", Assert.IsType<TextBlock>(blocks[0]).Text);
        Assert.Equal("After", Assert.IsType<TextBlock>(blocks[1]).Text);
        Assert.IsType<ImageBlock>(blocks[2]);
    }

    [Fact]
    public void FromInterleavedText_SkipsAMalformedDataUri_WithoutThrowing()
    {
        var blocks = ContentBlocks.FromInterleavedText($"Before\n{Marker(0)}\nAfter", new List<string> { "not-a-data-uri" });

        Assert.All(blocks, b => Assert.IsType<TextBlock>(b));
        Assert.Equal(2, blocks.Count);
    }

    [Fact]
    public void FromInterleavedText_AppendsAnAssetRefWithNoMarker_InsteadOfDroppingIt()
    {
        // Stimulus text with an image but no "IMG:n" placeholder in it at all — can happen with
        // stimulus text stored by an older ingestion pass, before markers were always interleaved.
        var blocks = ContentBlocks.FromInterleavedText("Reading passage, no marker here.", new List<string> { TinyPngDataUri });

        Assert.Equal(2, blocks.Count);
        Assert.Equal("Reading passage, no marker here.", Assert.IsType<TextBlock>(blocks[0]).Text);
        Assert.IsType<ImageBlock>(blocks[1]);
    }

    [Fact]
    public void FromInterleavedText_WithNullOrEmptyText_ReturnsNoBlocks()
    {
        Assert.Empty(ContentBlocks.FromInterleavedText(null, new List<string>()));
        Assert.Empty(ContentBlocks.FromInterleavedText("", new List<string>()));
    }

    [Fact]
    public void FromImageGallery_ResolvesEveryValidAssetRefAsAnImageBlock()
    {
        var blocks = ContentBlocks.FromImageGallery(new List<string> { TinyPngDataUri, TinyPngDataUri });

        Assert.Equal(2, blocks.Count);
        Assert.All(blocks, b => Assert.IsType<ImageBlock>(b));
    }

    [Fact]
    public void FromImageGallery_SkipsMalformedRefs_ButKeepsTheValidOnes()
    {
        var blocks = ContentBlocks.FromImageGallery(new List<string> { TinyPngDataUri, "garbage", "" });

        var block = Assert.Single(blocks);
        Assert.IsType<ImageBlock>(block);
    }
}
