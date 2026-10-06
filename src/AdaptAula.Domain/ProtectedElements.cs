namespace AdaptAula.Domain;

/// <summary>Keys stored in <see cref="Assessment.LockedFields"/> (V2 §8). A locked element can never change in any
/// adaptation generated from the assessment; the validator turns a violation into an Error.</summary>
public static class ProtectedElements
{
    public const string EvaluatedContent = "content";
    public const string Criteria = "criteria";
    public const string CorrectAnswer = "correct_answer";
    public const string TotalPoints = "total_points";
    public const string Language = "language";
    public const string EssentialVocabulary = "essential_vocabulary";
    public const string QuestionCount = "question_count";
    public const string CognitiveDemand = "cognitive_demand";
    public const string Grade = "grade";

    public static readonly IReadOnlyList<string> All = new[]
    {
        EvaluatedContent, Criteria, CorrectAnswer, TotalPoints, Language,
        EssentialVocabulary, QuestionCount, CognitiveDemand, Grade
    };

    /// <summary>What a fresh upload protects until the teacher relaxes something.</summary>
    public static readonly IReadOnlyList<string> Defaults = new[]
    {
        EvaluatedContent, Criteria, CorrectAnswer, TotalPoints, Language, QuestionCount, CognitiveDemand, Grade
    };
}
