using AdaptAula.Domain;

namespace AdaptAula.Infrastructure.Ai;

/// <summary>
/// One resolved, human-readable instruction the AI must follow for this question — already
/// decided by the deterministic <c>PlanResolver</c>. The generator only rewrites text; it never
/// decides which adaptations apply.
/// </summary>
public record AppliedRuleInstruction(
    string RuleId, string Description, string Category, bool RequiresTeacherReview, bool ProposalOnly = false);

/// <summary>Everything the teacher locked before generating (V2 §8), passed to the AI as hard constraints.</summary>
public record ProtectedContext(
    IReadOnlyList<string> LockedElements,
    IReadOnlyList<string> ProtectedVocabulary,
    int QuestionCount);

public record AdaptationTextRequest(
    Question Question,
    IReadOnlyList<AppliedRuleInstruction> AppliedRules,
    int Level,
    string Language,
    bool IsCurricularChange,
    string? CurricularObjective,
    ProtectedContext? Protected = null,
    IReadOnlyList<string>? CurricularReferents = null);

/// <summary>A change that could alter what is assessed; kept apart from the adapted text so the teacher decides (V2 §23).</summary>
public record AdaptationProposal(string ProposedText, List<string> RuleIds, string Reason);

/// <summary>
/// The AI's draft for one question. Deliberately excludes Points and ExpectedAnswer — those are
/// always carried over from the original <see cref="Question"/> in the pipeline, which
/// structurally prevents the AI from changing them (spec §11 POINTS_CHANGED / ANSWER_CHANGED).
/// </summary>
/// <param name="QuestionId">Which request this answers — a batch call can't assume the model
/// preserved array order or answered every item, so callers must match on this rather than on
/// position. The generator implementation resolves it from the model's own numbering internally;
/// callers never deal with that numbering directly.</param>
public record AdaptationTextResponse(
    Guid QuestionId,
    string AdaptedText,
    ResponseMode ResponseMode,
    List<string> Supports,
    List<ChangeLogEntry> ChangeLog,
    List<string> Warnings,
    AdaptationProposal? Proposal = null);

/// <summary>Swappable AI provider boundary (spec: keep the concrete model/vendor out of the rest
/// of the pipeline so it can move from a free tier to a paid one without other changes).</summary>
public interface IAdaptationTextGenerator
{
    /// <summary>Adapts several questions in one call — the free-tier bottleneck is Gemini's
    /// requests-per-minute cap, not per-call latency, so batching questions into fewer, larger
    /// calls is what actually shortens a full-assessment generation, not parallelizing one-call-
    /// per-question. Each returned <see cref="AdaptationTextResponse.QuestionId"/> identifies which
    /// request it answers; a request the model failed to answer is simply absent from the result
    /// (not assumed by position), and the caller (the pipeline) decides the fallback for it.</summary>
    Task<IReadOnlyList<AdaptationTextResponse>> GenerateBatchAsync(
        IReadOnlyList<AdaptationTextRequest> requests, CancellationToken ct = default);
}
