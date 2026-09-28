using System.Text.RegularExpressions;
using AdaptAula.Domain;
using AdaptAula.Infrastructure.Ai;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace AdaptAula.Infrastructure.Ingestion;

public class IngestionResult
{
    public required Assessment Assessment { get; init; }

    /// <summary>0-1 confidence in the extraction, self-reported by the AI. Below 0.6 the API flags
    /// LOW_EXTRACTION_CONFIDENCE and asks the teacher to compare with the original (spec §11).</summary>
    public required double ExtractionConfidence { get; init; }
}

/// <summary>
/// INGEST step (spec §7). Structuring is delegated to <see cref="IDocumentStructureExtractor"/> —
/// it reads the document's real text, layout and embedded images together and returns the question
/// structure directly, rather than us reconstructing reading order from word positions and pattern-
/// matching question boundaries with regexes. That approach (this class's previous implementation)
/// had no actual understanding of the document: every layout it hadn't been specifically taught to
/// handle broke it in a new way. This class now only does what's mechanical and reliable — pulling
/// a DOCX's plain text out of its XML, and pulling a PDF's embedded images out as candidate assets —
/// and maps the AI's structured response onto the domain model.
/// </summary>
public class DocumentIngestionService
{
    private readonly IDocumentStructureExtractor _extractor;

    public DocumentIngestionService(IDocumentStructureExtractor extractor)
    {
        _extractor = extractor;
    }

    public async Task<IngestionResult> IngestPlainTextAsync(string title, string text, CancellationToken ct = default)
    {
        var structure = await _extractor.ExtractFromTextAsync(text, ct);
        return BuildResult(title, null, structure, new List<CandidateImage>());
    }

    public async Task<IngestionResult> IngestDocxAsync(string fileName, Stream stream, CancellationToken ct = default)
    {
        var text = ExtractDocxText(stream);
        var structure = await _extractor.ExtractFromTextAsync(text, ct);
        return BuildResult(fileName, fileName, structure, new List<CandidateImage>());
    }

    public async Task<IngestionResult> IngestPdfAsync(string fileName, Stream stream, CancellationToken ct = default)
    {
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, ct);
        var pdfBytes = buffer.ToArray();

        List<CandidateImage> candidateImages;
        using (var pdf = PdfDocument.Open(pdfBytes))
        {
            candidateImages = ExtractCandidateImages(pdf);
        }

        var structure = await _extractor.ExtractFromPdfAsync(pdfBytes, ct);
        AssignImagesByPage(structure, candidateImages);
        return BuildResult(fileName, fileName, structure, candidateImages);
    }

    /// <summary>Attaches each candidate image to whichever section's reported page range covers the
    /// page it came from — see <see cref="IDocumentStructureExtractor"/>'s remarks for why this is a
    /// plain page-number check rather than asking the AI to visually match images itself.</summary>
    private static void AssignImagesByPage(DocumentStructure structure, List<CandidateImage> candidateImages)
    {
        foreach (var image in candidateImages)
        {
            var section = structure.Sections.FirstOrDefault(s => image.PageNumber >= s.StartPage && image.PageNumber <= s.EndPage);
            section?.StimulusImageRefs.Add(image.Index);
        }
    }

    private static IngestionResult BuildResult(
        string title, string? sourceFileName, DocumentStructure structure, List<CandidateImage> candidateImages)
    {
        var imageDataUris = candidateImages.Select(ToDataUri).ToList();
        var claimed = new HashSet<int>();

        // Defensive against the model referencing an index it shouldn't (out of range, or claimed
        // twice) — silently ignore rather than let one bad reference fail the whole upload.
        List<string> ResolveImageRefs(List<int> refs)
        {
            var resolved = new List<string>();
            foreach (var idx in refs.Distinct())
            {
                if (idx < 0 || idx >= imageDataUris.Count) continue;
                claimed.Add(idx);
                resolved.Add(imageDataUris[idx]);
            }
            return resolved;
        }

        var sections = new List<Section>();
        var globalOrder = 0;
        for (var i = 0; i < structure.Sections.Count; i++)
        {
            var draft = structure.Sections[i];
            var questions = draft.Questions.Select(q => ToQuestion(q, globalOrder++, ResolveImageRefs)).ToList();

            // Resolve images before the text, so [IMG] placeholders can be replaced with a resolved
            // reference to a specific entry in this section's own AssetRefs — the same content stays
            // interleaved in reading order instead of splitting into a separate text blob plus a
            // gallery of images underneath it.
            var stimulusAssetRefs = ResolveImageRefs(draft.StimulusImageRefs);
            var stimulusText = InterleaveImageMarkers(draft.StimulusText ?? "", stimulusAssetRefs).Trim();

            var section = new Section
            {
                Title = structure.Sections.Count > 1 ? $"Grupo {i + 1}" : "General",
                Order = i,
                StimulusText = stimulusText.Length == 0 ? null : stimulusText,
                AssetRefs = stimulusAssetRefs,
                Questions = questions
            };
            foreach (var q in section.Questions) q.SectionId = section.Id;
            sections.Add(section);
        }

        // Candidate images the model never tied to any section/question — kept in the data model as
        // a fallback (never silently dropped) even though the Analysis screen doesn't surface them.
        var unclaimedImages = imageDataUris.Where((_, idx) => !claimed.Contains(idx)).ToList();

        var assessment = new Assessment
        {
            Title = title,
            SourceFileName = sourceFileName,
            TotalPoints = sections.SelectMany(s => s.Questions).Sum(q => q.Points),
            ImageDataUris = unclaimedImages,
            Sections = sections
        };
        foreach (var section in sections) section.AssessmentId = assessment.Id;

        return new IngestionResult { Assessment = assessment, ExtractionConfidence = structure.Confidence };
    }

    private static Question ToQuestion(ExtractedQuestion extracted, int order, Func<List<int>, List<string>> resolveImageRefs)
    {
        var options = extracted.Options.Select(o => LeadingOptionLabel.Replace(o, "").Trim()).ToList();
        return new Question
        {
            Order = order,
            OriginalText = ComposeOriginalText(extracted.Text, options),
            Type = QuestionType.OpenText,
            Options = options,
            Points = extracted.PointsHint ?? 0,
            AssetRefs = resolveImageRefs(extracted.ImageRefs)
        };
    }

    /// <summary>An option's own leading "A. "/"B) " label, if the model included one despite being
    /// asked not to (see the extractor's prompt) — stripped defensively so it's never doubled up
    /// with the letter <see cref="ComposeOriginalText"/> adds.</summary>
    private static readonly Regex LeadingOptionLabel = new(@"^\s*[A-Za-z][.\)]\s+", RegexOptions.Compiled);

    /// <summary>Keeps the same visual shape the Analysis screen has always shown — the stem followed
    /// by lettered options.</summary>
    private static string ComposeOriginalText(string text, List<string> options)
    {
        if (options.Count == 0) return text;
        const string letters = "ABCDEFGHIJ";
        var optionLines = options.Select((opt, i) => $"{letters[i % letters.Length]}. {opt}");
        return text + "\n" + string.Join("\n", optionLines);
    }

    private static string ToDataUri(CandidateImage image) =>
        $"data:{image.MimeType};base64,{Convert.ToBase64String(image.Bytes)}";

    /// <summary>The extractor is asked to mark exactly where an image occurs in the reading flow
    /// with a literal "[IMG]" placeholder (see the prompt). Here that placeholder is replaced, in
    /// order, with a resolved reference to this section's own <c>AssetRefs</c> — so the Analysis
    /// screen can render text and images interleaved the way the original document actually reads,
    /// instead of a text blob followed by a separate image gallery. Any resolved image beyond the
    /// number of placeholders present (the model missed marking it, or didn't have one to place) is
    /// still appended at the end rather than dropped.</summary>
    private static readonly Regex ImagePlaceholder = new(@"\[IMG\]", RegexOptions.Compiled);

    private static string InterleaveImageMarkers(string text, List<string> assetRefs)
    {
        var segments = ImagePlaceholder.Split(text);
        var nextImage = 0;
        var result = segments[0];
        for (var i = 1; i < segments.Length; i++)
        {
            if (nextImage < assetRefs.Count) result += $"\n{ResolvedImageMarker(nextImage++)}\n";
            result += segments[i];
        }
        while (nextImage < assetRefs.Count) result += $"\n{ResolvedImageMarker(nextImage++)}\n";
        return result;
    }

    // A Private Use Area codepoint, not a control character: a NUL byte (\u0000) here silently
    // truncated at the database round-trip (SQLite's driver marshals TEXT as a null-terminated C
    // string) — everything from the first marker onward, images and all following text alike, was
    // being lost on the very first page load despite looking correct right after ingestion.
    private static string ResolvedImageMarker(int assetRefIndex) => $"IMG:{assetRefIndex}";

    private static string ExtractDocxText(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document.Body;
        if (body is null) return string.Empty;

        var paragraphs = body.Elements<Paragraph>()
            .Select(p => p.InnerText)
            .ToList();

        return string.Join("\n\n", CollapseConsecutiveBlanks(paragraphs));
    }

    private static IEnumerable<string> CollapseConsecutiveBlanks(List<string> paragraphs)
    {
        var lastWasBlank = false;
        foreach (var p in paragraphs)
        {
            var isBlank = string.IsNullOrWhiteSpace(p);
            if (isBlank && lastWasBlank) continue;
            yield return p;
            lastWasBlank = isBlank;
        }
    }

    /// <summary>Photos, diagrams and charts embedded in a page — kept as candidate assets and matched
    /// to a section by page number (see <see cref="AssignImagesByPage"/>). Small embedded images
    /// (icons, bullet glyphs) are skipped as decorative noise before that even happens.</summary>
    private const double MinImageDimensionPoints = 40;

    /// <summary>Some layouts embed the same graphic dozens of times — a per-question number badge, a
    /// repeating banner icon — large enough to pass the size filter above but byte-identical every
    /// time, unlike a genuine content photo (a real picture never legitimately repeats verbatim
    /// throughout a document). Filtered out so it's never wrongly attached to a section as content.</summary>
    private const int RepeatedImageThreshold = 3;

    private readonly record struct PositionedImage(byte[] Bytes, string MimeType, double VerticalCenter, int PageNumber);

    private static List<CandidateImage> ExtractCandidateImages(PdfDocument pdf)
    {
        var pageImages = FilterOutRepeatingDecorativeImages(pdf.GetPages().Select(ExtractPageImages).ToList());
        var index = 0;
        return pageImages
            .SelectMany(images => images)
            .Select(img => new CandidateImage(index++, img.Bytes, img.MimeType, img.PageNumber))
            .ToList();
    }

    private static List<List<PositionedImage>> FilterOutRepeatingDecorativeImages(List<List<PositionedImage>> pageImages)
    {
        var frequency = pageImages.SelectMany(images => images)
            .GroupBy(i => Convert.ToBase64String(i.Bytes))
            .ToDictionary(g => g.Key, g => g.Count());

        return pageImages
            .Select(images => images.Where(i => frequency[Convert.ToBase64String(i.Bytes)] < RepeatedImageThreshold).ToList())
            .ToList();
    }

    private static List<PositionedImage> ExtractPageImages(Page page)
    {
        var images = new List<PositionedImage>();
        foreach (var image in page.GetImages())
        {
            if (image.Bounds.Width < MinImageDimensionPoints || image.Bounds.Height < MinImageDimensionPoints) continue;

            try
            {
                byte[]? bytes = null;
                var mimeType = "image/png";
                if (image.TryGetPng(out var png))
                {
                    bytes = png;
                }
                else if (image.TryGetBytesAsMemory(out var raw) && raw.Length > 0)
                {
                    bytes = raw.ToArray();
                    mimeType = "image/jpeg";
                }
                else if (IsJpegEncoded(image) && image.RawMemory.Length > 0)
                {
                    // PdfPig's higher-level accessors can fail on some JPEG (DCTDecode) streams even
                    // though the raw stream bytes ARE already a complete, valid JPEG file — that's
                    // what DCTDecode means — so serve them directly rather than losing a real photo.
                    bytes = image.RawMemory.ToArray();
                    mimeType = "image/jpeg";
                }

                if (bytes is not null && !LooksLikeFlatDecorativeFill(bytes))
                {
                    images.Add(new PositionedImage(bytes, mimeType, (image.Bounds.Top + image.Bounds.Bottom) / 2, page.Number));
                }
            }
            catch
            {
                // Best-effort: an image PdfPig can't decode is skipped rather than failing the
                // whole upload over a picture that wasn't essential to begin with.
            }
        }
        // Top-to-bottom reading order, so the candidate-image numbering lines up with how a person
        // would naturally encounter them while reading the document.
        return images.OrderByDescending(i => i.VerticalCenter).ToList();
    }

    private static bool IsJpegEncoded(IPdfImage image)
    {
        try
        {
            return image.ImageDictionary.Data.TryGetValue("Filter", out var filter) &&
                   (filter?.ToString()?.Contains("DCTDecode") ?? false);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Many layouts use small solid-color raster fills as decorative backgrounds for a text
    /// box or highlight panel (confirmed against a real document: every embedded FlateDecode image
    /// on one page turned out to be a single flat color, while every genuine photo was a separate
    /// DCTDecode/JPEG stream). These pass the size filter — a background panel can be as large as the
    /// text box it sits behind — so they need their own check: decode the image and sample a coarse
    /// grid of pixels; if the color barely varies, it's a fill, not content. Sampling is restricted to
    /// the central region rather than the full image — also confirmed against a real document, a
    /// decorative panel with just a thin border/edge highlight around an otherwise flat fill (the
    /// whole passage box's outer frame) has enough edge-vs-fill variance across the FULL image to
    /// slip past this check; its interior alone is still flat, the same as a real photo's is not.</summary>
    private const int FlatColorSampleStep = 7;
    private const int FlatColorRangeThreshold = 16;
    private const double FlatColorSampleMarginFraction = 0.125;

    private static bool LooksLikeFlatDecorativeFill(byte[] imageBytes)
    {
        try
        {
            using var image = Image.Load<Rgb24>(imageBytes);
            var marginX = (int)(image.Width * FlatColorSampleMarginFraction);
            var marginY = (int)(image.Height * FlatColorSampleMarginFraction);

            byte minR = 255, minG = 255, minB = 255, maxR = 0, maxG = 0, maxB = 0;
            for (var y = marginY; y < image.Height - marginY; y += FlatColorSampleStep)
            {
                for (var x = marginX; x < image.Width - marginX; x += FlatColorSampleStep)
                {
                    var pixel = image[x, y];
                    if (pixel.R < minR) minR = pixel.R;
                    if (pixel.R > maxR) maxR = pixel.R;
                    if (pixel.G < minG) minG = pixel.G;
                    if (pixel.G > maxG) maxG = pixel.G;
                    if (pixel.B < minB) minB = pixel.B;
                    if (pixel.B > maxB) maxB = pixel.B;
                }
            }
            var range = Math.Max(maxR - minR, Math.Max(maxG - minG, maxB - minB));
            return range < FlatColorRangeThreshold;
        }
        catch
        {
            // If we can't even decode it to check, don't punish it for that — let it through and
            // let the teacher judge it in the Analysis screen rather than silently losing it.
            return false;
        }
    }
}
