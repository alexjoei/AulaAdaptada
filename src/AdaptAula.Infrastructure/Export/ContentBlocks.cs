using System.Text.RegularExpressions;

namespace AdaptAula.Infrastructure.Export;

public abstract record ContentBlock;
public sealed record TextBlock(string Text) : ContentBlock;
public sealed record ImageBlock(byte[] Bytes, string MimeType) : ContentBlock;

/// <summary>Turns the same "IMG:n" marker text used by ingestion (<c>DocumentIngestionService</c>)
/// and the Analysis screen (<c>IMAGE_MARKER</c> in Analysis.tsx) into an ordered list of text/image
/// blocks a renderer (PDF or DOCX) can lay out without knowing anything about the marker format
/// itself.</summary>
public static class ContentBlocks
{
    // The marker is wrapped in U+E000 (Private Use Area, invisible in any font) on both sides — see
    // DocumentIngestionService.ResolvedImageMarker and Analysis.tsx's IMAGE_MARKER. Matching only the
    // bare "IMG:n" still finds every marker (it's a substring either way), but without consuming the
    // wrapper too, each invisible delimiter is left stuck to the adjacent text block instead of the
    // match — harmless in a browser (CSS just doesn't render it) but liable to render as a tofu/blank
    // glyph in a PDF/DOCX font that does define something at that codepoint.
    private static readonly Regex ImageMarker = new("IMG:(\\d+)", RegexOptions.Compiled);

    /// <summary>Splits stimulus text on its embedded "IMG:n" markers, resolving each index against
    /// <paramref name="assetRefs"/>. An index with no matching asset (out of range, or the ref
    /// failed to decode) is silently skipped rather than breaking the whole render.</summary>
    public static List<ContentBlock> FromInterleavedText(string? text, IReadOnlyList<string> assetRefs)
    {
        var blocks = new List<ContentBlock>();
        if (string.IsNullOrEmpty(text)) return blocks;

        var referenced = new HashSet<int>();
        var lastEnd = 0;
        foreach (Match match in ImageMarker.Matches(text))
        {
            var before = text[lastEnd..match.Index].Trim();
            if (before.Length > 0) blocks.Add(new TextBlock(before));

            var index = int.Parse(match.Groups[1].Value);
            referenced.Add(index);
            if (index >= 0 && index < assetRefs.Count && TryParseDataUri(assetRefs[index], out var bytes, out var mimeType))
                blocks.Add(new ImageBlock(bytes, mimeType));

            lastEnd = match.Index + match.Length;
        }

        var tail = text[lastEnd..].Trim();
        if (tail.Length > 0) blocks.Add(new TextBlock(tail));

        // Defensive against stimulus text stored without a matching marker for every asset (e.g. an
        // older ingestion pass) — an image never dropped silently, appended in index order instead,
        // the same fallback ingestion's own InterleaveImageMarkers already applies on write.
        for (var i = 0; i < assetRefs.Count; i++)
        {
            if (referenced.Contains(i)) continue;
            if (TryParseDataUri(assetRefs[i], out var bytes, out var mimeType))
                blocks.Add(new ImageBlock(bytes, mimeType));
        }

        return blocks;
    }

    /// <summary>A flat gallery of images with no surrounding text — the per-question
    /// <c>Question.AssetRefs</c> case, matching the plain image gallery Analysis.tsx renders under
    /// a question's text.</summary>
    public static List<ContentBlock> FromImageGallery(IReadOnlyList<string> assetRefs)
    {
        var blocks = new List<ContentBlock>();
        foreach (var assetRef in assetRefs)
        {
            if (TryParseDataUri(assetRef, out var bytes, out var mimeType))
                blocks.Add(new ImageBlock(bytes, mimeType));
        }
        return blocks;
    }

    /// <summary>Parses a "data:{mime};base64,{data}" URI, the format <c>DocumentIngestionService.ToDataUri</c>
    /// always produces. Malformed input is reported via the return value, not an exception, so one bad
    /// asset never fails the whole export.</summary>
    private static bool TryParseDataUri(string dataUri, out byte[] bytes, out string mimeType)
    {
        bytes = Array.Empty<byte>();
        mimeType = string.Empty;

        if (string.IsNullOrEmpty(dataUri) || !dataUri.StartsWith("data:", StringComparison.Ordinal)) return false;

        var comma = dataUri.IndexOf(',');
        if (comma < 0) return false;

        var header = dataUri[5..comma]; // "{mime};base64"
        var semicolon = header.IndexOf(';');
        if (semicolon < 0) return false;

        mimeType = header[..semicolon];
        try
        {
            bytes = Convert.FromBase64String(dataUri[(comma + 1)..]);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
