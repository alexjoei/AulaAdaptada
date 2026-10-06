namespace AdaptAula.Domain;

public enum PlanStatus
{
    Draft,
    Planned,
    Generated,
    NeedsTeacherReview,
    Approved,
    Exported
}

/// <summary>Output of the deterministic PLAN step (spec §7): which atomic rules apply, per question.
/// No text has been rewritten yet at this point.</summary>
public class AdaptationPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentId { get; set; }
    public Guid ProfileId { get; set; }
    public int Level { get; set; } = 1;
    public PlanStatus Status { get; set; } = PlanStatus.Draft;

    /// <summary>True only when the teacher explicitly supplied curricular referents/objectives for this plan.
    /// Never set from a profile/necessity alone (spec §1, §5 "NO inferir nivel curricular por diagnóstico").</summary>
    public bool IsCurricularChange { get; set; }
    public string? CurricularObjective { get; set; }

    public List<string> Locks { get; set; } = new();

    /// <summary>Resolved rules per question id.</summary>
    public Dictionary<Guid, List<ResolvedRule>> ResolvedRulesByQuestion { get; set; } = new();

    public List<string> Warnings { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Course/cycle the teacher chose as curricular reference when the adaptation-curricular switch is on (V2 §14).</summary>
    public string? CurricularReference { get; set; }
    public List<string> CurricularCriteriaIds { get; set; } = new();
    public List<string> CurricularContentIds { get; set; } = new();

    /// <summary>Per-student parameter values copied from the profile when the plan was made, so later
    /// profile edits never silently change an adaptation already generated.</summary>
    public Dictionary<string, string> ProfileSettings { get; set; } = new();

    /// <summary>Merged look of the document (from the style measures), editable in the visual editor (V2 §9).</summary>
    public DocumentStyle Style { get; set; } = new();

    /// <summary>The editable document. Null until generated; once the teacher edits it, this HTML is what gets exported.</summary>
    public string? DocumentHtml { get; set; }
    public DateTime? DocumentEditedAt { get; set; }

    public Guid? PackId { get; set; }
}

/// <summary>Output of TRANSFORM (spec §7): the AI-rewritten question, plus VALIDATE results attached later.
/// Points and ExpectedAnswer are always carried over from the original Question in code — the AI response
/// schema does not include them, so it cannot alter them (structural guard against POINTS_CHANGED/ANSWER_CHANGED).</summary>
public class AdaptedQuestion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public Guid QuestionId { get; set; }
    public string AdaptedText { get; set; } = string.Empty;
    public ResponseMode ResponseMode { get; set; } = ResponseMode.Written;
    public List<string> Supports { get; set; } = new();
    public int Points { get; set; }
    public List<ChangeLogEntry> ChangeLog { get; set; } = new();
    public bool TeacherApproved { get; set; }

    /// <summary>A change the AI judged able to alter what is assessed. It is NOT applied to <see cref="AdaptedText"/>;
    /// the teacher accepts or rejects it (V2 §23).</summary>
    public QuestionProposal? Proposal { get; set; }
}

public enum ProposalStatus
{
    Pending,
    Accepted,
    Rejected
}

public class QuestionProposal
{
    public string ProposedText { get; set; } = string.Empty;
    public List<string> RuleIds { get; set; } = new();
    public string Reason { get; set; } = string.Empty;
    public ProposalStatus Status { get; set; } = ProposalStatus.Pending;
}

public class ChangeLogEntry
{
    public string RuleId { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;

    /// <summary>The fragment as it was / as it became, when the AI could point to it. Lets the comparator tie a
    /// highlighted change to the measure that caused it (V2 §15).</summary>
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
}

public class ValidationResult
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PlanId { get; set; }
    public ValidationSeverity Severity { get; set; }
    public ValidationCode Code { get; set; }
    public Guid? QuestionId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? OriginalValue { get; set; }
    public string? AdaptedValue { get; set; }
    public bool RequiresReview { get; set; }
}

public enum SemaphoreLevel
{
    Green,
    Orange,
    Red
}

public class ExportVersion
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssessmentId { get; set; }
    public Guid ProfileId { get; set; }
    public Guid PlanId { get; set; }
    public ExportFormat Format { get; set; }
    public string ApprovedBy { get; set; } = string.Empty;
    public DateTime ApprovedAt { get; set; } = DateTime.UtcNow;
    public string Hash { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
}
