namespace AdaptAula.Domain;

public class Assessment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public int Grade { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Language { get; set; } = "es";
    public int TotalPoints { get; set; }
    public string? SourceFileName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Heuristic confidence (0-1) from the INGEST step. Below 0.6 the validator raises
    /// LOW_EXTRACTION_CONFIDENCE so the teacher compares against the original file (spec §11).</summary>
    public double? ExtractionConfidence { get; set; }

    /// <summary>Field names the teacher has locked (e.g. "grade","content","criteria","total_points","language").
    /// Locks always win over any rule or preset (spec §6 precedence).</summary>
    public List<string> LockedFields { get; set; } = new();

    /// <summary>Images embedded in the source document (photos, diagrams, charts) as data URIs, in
    /// document order. MVP1 shows these alongside the text so the teacher can see the same visual
    /// context as the original (spec questions/passages that rely on a photo or diagram aren't
    /// otherwise represented) — they aren't yet tied to a specific section/question.</summary>
    public List<string> ImageDataUris { get; set; } = new();

    public List<Section> Sections { get; set; } = new();
}

public class Section
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }

    /// <summary>Shared reading passage/instructions this section's questions refer to (e.g. a
    /// reading-comprehension text), shown once above the questions rather than repeated per
    /// question. Null when the section has no shared stimulus.</summary>
    public string? StimulusText { get; set; }

    /// <summary>Images from the source document positioned within this section's shared passage
    /// (e.g. photos illustrating a reading-comprehension text), rather than within any one
    /// question. See <see cref="Question.AssetRefs"/> for the per-question equivalent.</summary>
    public List<string> AssetRefs { get; set; } = new();

    public List<Question> Questions { get; set; } = new();
}

public class Question
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SectionId { get; set; }
    public int Order { get; set; }
    public string OriginalText { get; set; } = string.Empty;
    public QuestionType Type { get; set; } = QuestionType.OpenText;
    public int Points { get; set; }
    public string? ExpectedAnswer { get; set; }

    /// <summary>What this question actually measures (spec §1: separate constructo evaluado from vía de acceso).
    /// Populated by the teacher during the Analysis screen (ANNOTATE step), never inferred silently.</summary>
    public List<string> ConstructTags { get; set; } = new();

    public List<string> Options { get; set; } = new();

    /// <summary>Images from the source document positioned within this specific question (e.g. a
    /// diagram the question refers to directly), as data URIs. Populated during ingestion by
    /// matching each embedded image's position in the document to the question whose text
    /// surrounds it; images that can't be confidently placed fall back to
    /// <see cref="Assessment.ImageDataUris"/> instead. See <see cref="Section.AssetRefs"/> for
    /// images belonging to a shared passage rather than one question.</summary>
    public List<string> AssetRefs { get; set; } = new();
}
