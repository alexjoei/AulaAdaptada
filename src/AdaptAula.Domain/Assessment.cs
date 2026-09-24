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

    public List<Section> Sections { get; set; } = new();
}

public class Section
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
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
    public List<string> AssetRefs { get; set; } = new();
}
