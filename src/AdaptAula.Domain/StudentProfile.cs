namespace AdaptAula.Domain;

/// <summary>A configurable educational measure, not a diagnosis. The unit the rules engine
/// actually operates on (spec §1: "la unidad real del sistema es la medida educativa configurable").</summary>
public class StudentProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Alias/code only — never a real name or diagnosis field (privacy by design, spec §1).</summary>
    public string Alias { get; set; } = string.Empty;

    public int? Grade { get; set; }

    /// <summary>Only set when a teacher has explicitly defined curricular referents for this student.
    /// The system must never infer this from a "necessity"/diagnosis label (spec §5, Discapacidad intelectual).</summary>
    public string? CurricularLevelOverride { get; set; }

    /// <summary>Necessity presets selected for this student, e.g. "dyslexia","adhd","tdl_tel","asd",
    /// "gifted". Each expands to atomic rules via <see cref="RulesEngine"/> — never hardcoded per-profile logic.</summary>
    public List<string> Measures { get; set; } = new();

    /// <summary>Individual overrides that beat the necessity preset for this specific student
    /// (spec §6 precedence: medida individual > preset de necesidad).</summary>
    public List<string> Accommodations { get; set; } = new();

    /// <summary>Explicit opt-outs: atomic rule ids this student's plan must never apply even if the preset would.</summary>
    public List<string> Exceptions { get; set; } = new();

    public DateTime ReviewDate { get; set; } = DateTime.UtcNow;
}
