namespace AdaptAula.Domain;

/// <summary>The ten groups a measure is shown under once a need is selected (V2 §2).</summary>
public enum MeasureGroup
{
    Presentation,
    Reading,
    Writing,
    Language,
    Attention,
    Math,
    Time,
    Response,
    Visual,
    Evaluation
}

/// <summary>How a measure is carried out. Only <see cref="Text"/> and <see cref="Support"/> are sent
/// to the AI; <see cref="Style"/> is applied deterministically by the renderer and
/// <see cref="Logistics"/> (time, response mode, human help) is reported to the teacher, never to the AI.</summary>
public enum MeasureKind
{
    Text,
    Support,
    Style,
    Logistics
}

/// <summary>How a need relates to a measure (V2 §3).</summary>
public enum MeasureClassification
{
    Recommended,
    Optional,
    RequiresTeacherDecision,
    NotRecommended
}

public class MeasureParameter
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;

    /// <summary>"number" | "text" | "choice".</summary>
    public string Type { get; set; } = "number";

    public string Default { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public List<string> Choices { get; set; } = new();
}

/// <summary>Document-level look. Measures contribute partial styles; the plan stores the merged result
/// (editable later by the teacher in the visual editor).</summary>
public class DocumentStyle
{
    public string FontFamily { get; set; } = "Arial";
    public double FontSizePt { get; set; } = 11;
    public double LineSpacing { get; set; } = 1.2;
    public double LetterSpacingPt { get; set; }
    public double WordSpacingPt { get; set; }
    public double ParagraphSpacingPt { get; set; } = 4;
    public string Align { get; set; } = "left";
    public double MarginMm { get; set; } = 18;
    public bool PageBreakPerQuestion { get; set; }
    public bool HighContrast { get; set; }
    public bool ChecklistSupports { get; set; }
    public double AnswerSpaceFactor { get; set; } = 1;
    public bool RuledAnswerLines { get; set; }
    public bool GridAnswerSpace { get; set; }
    public bool HideDecorativeImages { get; set; }
}

public class NeedMeasure
{
    public string RuleId { get; set; } = string.Empty;
    public MeasureClassification Classification { get; set; } = MeasureClassification.Recommended;
}
