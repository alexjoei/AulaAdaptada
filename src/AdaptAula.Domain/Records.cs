namespace AdaptAula.Domain;

public enum TeacherDecision
{
    Pending,
    Accepted,
    Rejected,
    Edited
}

/// <summary>Audit trail of every change and decision (V2 §21): before/after, applied rule, reason, risk,
/// teacher decision and date. Lets a teacher reconstruct why a test ended up the way it did.</summary>
public class ChangeLogRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public Guid? QuestionId { get; set; }

    /// <summary>generated | proposal | review | undo | edit | text_tool | document | export.</summary>
    public string Kind { get; set; } = "generated";

    public string RuleId { get; set; } = string.Empty;
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public RiskLevel Risk { get; set; } = RiskLevel.Low;
    public TeacherDecision Decision { get; set; } = TeacherDecision.Pending;
    public string Actor { get; set; } = string.Empty;
    public DateTime At { get; set; } = DateTime.UtcNow;
}

/// <summary>A measure the teacher defined herself (V2 §3). Its instruction is sent to the AI like any other measure.</summary>
public class CustomMeasure
{
    public string Id { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public MeasureGroup Group { get; set; } = MeasureGroup.Presentation;
    public MeasureKind Kind { get; set; } = MeasureKind.Text;
    public bool AltersAssessedConstruct { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>One test adapted for several students at once (V2 §19).</summary>
public class ClassPack
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public List<Guid> PlanIds { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
