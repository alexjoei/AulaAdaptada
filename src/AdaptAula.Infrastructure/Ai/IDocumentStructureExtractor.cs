namespace AdaptAula.Infrastructure.Ai;

/// <summary>An embedded image pulled out of the source document by PdfPig, together with the
/// 1-based page it came from — used to match it against whichever <see cref="ExtractedSection"/>'s
/// page range covers that page. Deliberately not matched by asking the AI to visually compare this
/// image against the document: that was tried (as separately-labeled reference images alongside the
/// PDF) and proved unreliable regardless of prompt or model tier — the model is excellent at reading
/// a document's text/structure but not at this kind of cross-referencing task. Page numbers, which
/// the model reads directly off the same document it's already parsing reliably, are a much sturdier
/// signal.</summary>
public record CandidateImage(int Index, byte[] Bytes, string MimeType, int PageNumber);

/// <summary>One question as the AI understood it: its full stem (and, for a true/false table, one
/// line per statement), its multiple-choice options if any, and an explicit points value if the
/// document states one.</summary>
public record ExtractedQuestion(string Text, List<string> Options, int? PointsHint, List<int> ImageRefs);

/// <summary>A group of questions that share a reading passage/instructions (<c>StimulusText</c>), or
/// just a plain run of questions with no shared stimulus. <c>StartPage</c>/<c>EndPage</c> (1-based,
/// inclusive) are the pages this section's shared text and questions appear on in the source PDF —
/// 0 for plain-text/DOCX ingestion, which has no page concept. <see cref="DocumentIngestionService"/>
/// uses them to attach each <see cref="CandidateImage"/> on a covered page to this section.</summary>
public record ExtractedSection(string? StimulusText, List<int> StimulusImageRefs, List<ExtractedQuestion> Questions, int StartPage = 0, int EndPage = 0);

/// <summary><c>Confidence</c> is the model's own 0-1 estimate of how reliable this extraction is —
/// surfaced to the teacher as LOW_EXTRACTION_CONFIDENCE below 0.6 (spec §11), the same as the old
/// heuristic pipeline's confidence score.</summary>
public record DocumentStructure(List<ExtractedSection> Sections, double Confidence);

/// <summary>
/// INGEST/PARSE step (spec §7): turns a source document into a starting Assessment structure.
/// Swappable AI provider boundary, same reasoning as <see cref="IAdaptationTextGenerator"/> — keep
/// the concrete model/vendor out of the rest of the pipeline.
/// </summary>
public interface IDocumentStructureExtractor
{
    Task<DocumentStructure> ExtractFromPdfAsync(byte[] pdfBytes, CancellationToken ct = default);

    Task<DocumentStructure> ExtractFromTextAsync(string text, CancellationToken ct = default);
}
