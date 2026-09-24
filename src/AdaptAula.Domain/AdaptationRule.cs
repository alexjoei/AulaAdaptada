namespace AdaptAula.Domain;

public enum ApplyMode
{
    Always,
    Default,
    Configurable,
    Never
}

/// <summary>An atomic, data-driven adaptation rule, seeded verbatim from the rules matrix
/// (AdaptAula_Matriz_Reglas.pdf). Rules are never hardcoded per-necessity in code — the
/// necessity presets are just named bundles of these ids (spec §6).</summary>
public class AdaptationRule
{
    /// <summary>Stable machine id, e.g. "instruction.max_actions=1", "typography.min_size=14".</summary>
    public string Id { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
    public RuleCategory Category { get; set; }
    public ApplyMode ApplyMode { get; set; } = ApplyMode.Default;

    /// <summary>Minimum adaptation level (1 Accessibility, 2 Scaffolding, 3 Personalized) at which this rule may apply.</summary>
    public int MinLevel { get; set; } = 1;

    public RiskLevel RiskLevel { get; set; } = RiskLevel.Low;

    /// <summary>Construct tags for which this rule must never apply (e.g. a "reading" question can't get read-aloud).
    /// Mirrors the "No aplicar si..." column of the matrix.</summary>
    public List<string> BlockedByConstructTags { get; set; } = new();

    public bool RequiresTeacherReview { get; set; }

    /// <summary>Other rule ids this one is known to conflict with; on conflict, the resolver keeps
    /// whichever preserves the construct better and emits UNSUPPORTED_CONFLICT.</summary>
    public List<string> ConflictsWith { get; set; } = new();

    public string DevNotes { get; set; } = string.Empty;
}

/// <summary>Where a resolved rule's instruction to apply/not-apply came from, used for the
/// precedence order in spec §6: LOCK docente > medida individual > regla de constructo > preset de necesidad > estilo global.</summary>
public enum RuleSource
{
    TeacherLock = 1,
    IndividualMeasure = 2,
    ConstructProtection = 3,
    NecessityPreset = 4,
    GlobalStyle = 5
}

public class ResolvedRule
{
    public string RuleId { get; set; } = string.Empty;
    public RuleSource Source { get; set; }
    public bool Applied { get; set; }
    public string Reason { get; set; } = string.Empty;
}

/// <summary>A named bundle of atomic rule ids for one necessity/profile preset (e.g. "dyslexia" -> its rule ids).
/// Presets are editable data, not branching logic.</summary>
public class NecessityPreset
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> RuleIds { get; set; } = new();
}
