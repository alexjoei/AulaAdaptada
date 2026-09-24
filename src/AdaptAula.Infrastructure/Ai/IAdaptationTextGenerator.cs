using AdaptAula.Domain;

namespace AdaptAula.Infrastructure.Ai;

/// <summary>
/// One resolved, human-readable instruction the AI must follow for this question — already
/// decided by the deterministic <c>PlanResolver</c>. The generator only rewrites text; it never
/// decides which adaptations apply.
/// </summary>
public record AppliedRuleInstruction(string RuleId, string Description, string Category, bool RequiresTeacherReview);

public record AdaptationTextRequest(
    Question Question,
    IReadOnlyList<AppliedRuleInstruction> AppliedRules,
    int Level,
    string Language,
    bool IsCurricularChange,
    string? CurricularObjective);

/// <summary>
/// The AI's draft for one question. Deliberately excludes Points and ExpectedAnswer — those are
/// always carried over from the original <see cref="Question"/> in the pipeline, which
/// structurally prevents the AI from changing them (spec §11 POINTS_CHANGED / ANSWER_CHANGED).
/// </summary>
public record AdaptationTextResponse(
    string AdaptedText,
    ResponseMode ResponseMode,
    List<string> Supports,
    List<ChangeLogEntry> ChangeLog,
    List<string> Warnings);

/// <summary>Swappable AI provider boundary (spec: keep the concrete model/vendor out of the rest
/// of the pipeline so it can move from a free tier to a paid one without other changes).</summary>
public interface IAdaptationTextGenerator
{
    Task<AdaptationTextResponse> GenerateAsync(AdaptationTextRequest request, CancellationToken ct = default);
}
