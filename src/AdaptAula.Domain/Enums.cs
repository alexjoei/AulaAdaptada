namespace AdaptAula.Domain;

public enum AdaptationLevel
{
    Accessibility = 1,
    Scaffolding = 2,
    Personalized = 3
}

public enum QuestionType
{
    OpenText,
    ShortAnswer,
    MultipleChoice,
    Classification,
    FillInTheBlank,
    Matching
}

public enum ResponseMode
{
    Written,
    Oral,
    Keyboard,
    Selection,
    Combined
}

public enum RuleCategory
{
    Structure,
    Instructions,
    Attention,
    SelfRegulation,
    Time,
    Typography,
    Layout,
    Language,
    Access,
    Response,
    Evaluation,
    Support,
    Problems,
    Orientation,
    Multimedia,
    Accessibility,
    Curricular,
    Text,
    Vocabulary,
    Planning,
    Translation,
    Deepening
}

public enum RiskLevel
{
    Low,
    Medium,
    High,
    Critical
}

public enum ValidationSeverity
{
    Error,
    Warning,
    Review
}

public enum ValidationCode
{
    PointsChanged,
    AnswerChanged,
    ContentDropped,
    ReadingConstructBypassed,
    SpellingConstructBypassed,
    HintRevealsAnswer,
    DifficultyReduced,
    CurricularChange,
    LowExtractionConfidence,
    UnsupportedConflict
}

public enum ExportFormat
{
    Docx,
    Pdf
}

/// <summary>Aspects of a question that a construct (what is actually being measured) may depend on.
/// Adaptations must never bypass a construct tag present on a question unless explicitly authorized.</summary>
public static class ConstructTags
{
    public const string Reading = "reading";
    public const string Spelling = "spelling";
    public const string Writing = "writing";
    public const string Calculation = "calculation";
    public const string Memory = "memory";
    public const string LinguisticComplexity = "linguistic_complexity";
    public const string MotorMode = "motor_mode";
    public const string TextStructure = "text_structure";
    public const string Vocabulary = "vocabulary";
    public const string LanguageDomain = "language_domain";
    public const string FigurativeLanguage = "figurative_language";
    public const string ListeningComprehension = "listening_comprehension";
}
