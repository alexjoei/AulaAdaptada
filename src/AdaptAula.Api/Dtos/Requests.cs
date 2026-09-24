using AdaptAula.Domain;

namespace AdaptAula.Api.Dtos;

public record CreateAssessmentFromTextRequest(string Title, string Text, int Grade, string Subject, string Language = "es");

public record UpdateAssessmentRequest(
    string? Title,
    int? Grade,
    string? Subject,
    List<string>? LockedFields,
    List<QuestionEditRequest>? Questions);

public record QuestionEditRequest(
    Guid Id,
    int? Points,
    string? ExpectedAnswer,
    List<string>? ConstructTags,
    QuestionType? Type);

public record CreateProfileRequest(
    string Alias,
    int? Grade,
    List<string> Measures,
    List<string> Accommodations,
    List<string> Exceptions);

public record CreatePlanRequest(
    Guid ProfileId,
    int Level = 1,
    bool CurricularChangeAuthorized = false,
    string? CurricularObjective = null);

public record ReviewChangeRequest(Guid AdaptedQuestionId, bool Approved);

public record ApprovePlanRequest(string ApprovedBy);
