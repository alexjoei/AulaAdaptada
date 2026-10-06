using AdaptAula.Domain;
using AdaptAula.Infrastructure.Ai;
using AdaptAula.RulesEngine;
using AdaptAula.Validation;
using Microsoft.Extensions.Options;

namespace AdaptAula.Api.Services;

/// <summary>
/// Orchestrates TRANSFORM + VALIDATE (spec §7) for an already-resolved <see cref="AdaptationPlan"/>.
/// Lives in the API project (not Infrastructure) because it composes the AI provider, the rules
/// catalog and the validator together — none of which need to know about each other directly.
/// </summary>
public class AdaptationPipelineService
{
    private readonly IAdaptationTextGenerator _generator;
    private readonly GenerationProgressTracker _progress;
    private readonly GeminiOptions _geminiOptions;
    private readonly ILogger<AdaptationPipelineService> _logger;

    public AdaptationPipelineService(
        IAdaptationTextGenerator generator, GenerationProgressTracker progress,
        IOptions<GeminiOptions> geminiOptions, ILogger<AdaptationPipelineService> logger)
    {
        _generator = generator;
        _progress = progress;
        _geminiOptions = geminiOptions.Value;
        _logger = logger;
    }

    public async Task<(List<AdaptedQuestion> Adapted, List<ValidationResult> Validation)> GenerateAndValidateAsync(
        Assessment assessment, AdaptationPlan plan, double? extractionConfidence, CancellationToken ct,
        IReadOnlyDictionary<string, AdaptationRule>? extraRules = null,
        IReadOnlyList<string>? curricularReferents = null)
    {
        var questionsById = assessment.Sections
            .SelectMany(s => s.Questions)
            .ToDictionary(q => q.Id);

        var protectedContext = new ProtectedContext(
            plan.Locks.ToList(), assessment.ProtectedVocabulary.ToList(), questionsById.Count);

        var adapted = new List<AdaptedQuestion>();
        var pendingRequests = new List<AdaptationTextRequest>();

        // First pass: settle every question that needs no AI call at all (nothing to change for it
        // at this level — keep the original text verbatim rather than spending a call rewriting it
        // into itself), and collect the rest into one flat list to batch below.
        foreach (var (questionId, resolvedRules) in plan.ResolvedRulesByQuestion)
        {
            if (!questionsById.TryGetValue(questionId, out var question))
                continue;

            // Only Text/Support measures are instructions for the AI. Style measures are rendered by the exporter and
            // Logistics measures (extra time, scribe…) are reported to the teacher — neither needs a rewrite.
            var appliedInstructions = resolvedRules
                .Where(r => r.Applied)
                .Select(r => (Resolved: r, Rule: Lookup(r.RuleId, extraRules)))
                .Where(x => x.Rule is not null && x.Rule.Kind is MeasureKind.Text or MeasureKind.Support)
                .Select(x => new AppliedRuleInstruction(
                    x.Rule!.Id, x.Rule.Description, x.Rule.Category.ToString(), x.Rule.RequiresTeacherReview, x.Resolved.ProposalOnly))
                .ToList();

            if (appliedInstructions.Count == 0 && !plan.IsCurricularChange)
            {
                adapted.Add(OriginalTextFallback(plan.Id, question));
                continue;
            }

            pendingRequests.Add(new AdaptationTextRequest(
                question, appliedInstructions, plan.Level, assessment.Language,
                plan.IsCurricularChange, plan.CurricularObjective, protectedContext, curricularReferents));
        }

        _progress.Start(plan.Id, pendingRequests.Count);
        try
        {
            // Batched rather than one call per question: the free tier's bottleneck is Gemini's
            // requests-per-minute cap, not per-call latency, so fewer/larger calls is what actually
            // shortens a full-assessment generation (see GeminiOptions.AdaptationBatchSize).
            foreach (var batch in pendingRequests.Chunk(Math.Max(1, _geminiOptions.AdaptationBatchSize)))
            {
                await GenerateBatchWithFallbackAsync(plan, batch, adapted, ct);
                _progress.Increment(plan.Id, batch.Length);
            }
        }
        finally
        {
            _progress.Complete(plan.Id);
        }

        var validation = SafetyValidator.Validate(plan, assessment, questionsById, adapted, extractionConfidence);
        return (adapted, validation);
    }

    private static AdaptationRule? Lookup(string id, IReadOnlyDictionary<string, AdaptationRule>? extra) =>
        RuleCatalog.ById.TryGetValue(id, out var rule) ? rule : extra?.GetValueOrDefault(id);

    private async Task GenerateBatchWithFallbackAsync(
        AdaptationPlan plan, IReadOnlyList<AdaptationTextRequest> batch, List<AdaptedQuestion> adapted, CancellationToken ct)
    {
        IReadOnlyList<AdaptationTextResponse> responses;
        try
        {
            responses = await _generator.GenerateBatchAsync(batch, ct);
        }
        catch (Exception ex)
        {
            // A whole-batch failure (network/parse) — every question in it fails closed onto its
            // original text rather than being dropped or guessed, same as a single-question failure.
            _logger.LogError(ex, "Batch adaptation generation failed for {Count} question(s)", batch.Count);
            foreach (var request in batch)
            {
                plan.Warnings.Add($"[{request.Question.Id}] HUMAN_REVIEW_REQUIRED: fallo generando la adaptación ({ex.Message}).");
                adapted.Add(OriginalTextFallback(plan.Id, request.Question));
            }
            return;
        }

        var responsesByQuestionId = responses.ToDictionary(r => r.QuestionId);
        foreach (var request in batch)
        {
            if (!responsesByQuestionId.TryGetValue(request.Question.Id, out var response))
            {
                // The model answered the batch but skipped this particular question — fail closed
                // on it individually rather than losing it or failing the rest of the batch too.
                plan.Warnings.Add($"[{request.Question.Id}] HUMAN_REVIEW_REQUIRED: la IA no devolvió una adaptación para esta pregunta.");
                adapted.Add(OriginalTextFallback(plan.Id, request.Question));
                continue;
            }

            adapted.Add(new AdaptedQuestion
            {
                PlanId = plan.Id,
                QuestionId = request.Question.Id,
                AdaptedText = response.AdaptedText,
                ResponseMode = response.ResponseMode,
                Supports = response.Supports,
                Points = request.Question.Points, // always the original — never taken from the AI response
                ChangeLog = response.ChangeLog,
                // A change that might alter what is assessed is parked here, never applied silently (V2 §23).
                Proposal = response.Proposal is { } proposal && proposal.ProposedText.Trim() != response.AdaptedText.Trim()
                    ? new QuestionProposal { ProposedText = proposal.ProposedText, RuleIds = proposal.RuleIds, Reason = proposal.Reason }
                    : null
            });

            foreach (var warning in response.Warnings)
                plan.Warnings.Add($"[{request.Question.Id}] {warning}");
        }
    }

    private static AdaptedQuestion OriginalTextFallback(Guid planId, Question question) => new()
    {
        PlanId = planId,
        QuestionId = question.Id,
        AdaptedText = question.OriginalText,
        Points = question.Points,
        ResponseMode = ResponseMode.Written
    };
}
