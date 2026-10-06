using AdaptAula.Domain;

namespace AdaptAula.Api.Dtos;

public record CreateAssessmentFromTextRequest(string Title, string Text, int? Grade, string? Subject, string Language = "es");

public record UpdateAssessmentRequest(
    string? Title,
    int? Grade,
    string? Subject,
    List<string>? LockedFields,
    List<QuestionEditRequest>? Questions,
    List<string>? ProtectedVocabulary = null,
    string? CurriculumId = null,
    string? CurriculumAreaId = null);

public record QuestionEditRequest(
    Guid Id,
    int? Points,
    string? ExpectedAnswer,
    List<string>? ConstructTags,
    QuestionType? Type,
    QuestionAnalysis? Analysis = null);

public record CreateProfileRequest(
    string Alias,
    int? Grade,
    List<string> Measures,
    List<string> Accommodations,
    List<string> Exceptions,
    Dictionary<string, string>? Settings = null);

public record EffectiveMeasuresRequest(
    List<string> Needs,
    List<string> Accommodations,
    List<string> Exceptions,
    int SchemaVersion = 2);

public record CreateCustomMeasureRequest(string Description, MeasureGroup Group, MeasureKind Kind, bool AltersAssessedConstruct = true);

public record CreatePlanRequest(
    Guid ProfileId,
    int Level = 1,
    bool CurricularChangeAuthorized = false,
    string? CurricularObjective = null,
    string? CurricularReference = null,
    List<string>? CurricularCriteriaIds = null,
    List<string>? CurricularContentIds = null);

public record ReviewChangeRequest(Guid AdaptedQuestionId, bool Approved);

public record ApprovePlanRequest(string ApprovedBy);

public record UpdateAdaptedQuestionRequest(string AdaptedText, List<string>? Supports = null, string Kind = "edit", string? RuleId = null);

public record ProposalDecisionRequest(bool Accept);

public record TextToolLogRequest(Guid? QuestionId, string Tool, string Before, string After);

public record SaveDocumentRequest(string Html, DocumentStyle? Style = null);
