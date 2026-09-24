using AdaptAula.Domain;
using AdaptAula.Infrastructure.Ai;
using AdaptAula.RulesEngine;
using AdaptAula.Validation;

namespace AdaptAula.Api.Services;

/// <summary>
/// Orchestrates TRANSFORM + VALIDATE (spec §7) for an already-resolved <see cref="AdaptationPlan"/>.
/// Lives in the API project (not Infrastructure) because it composes the AI provider, the rules
/// catalog and the validator together — none of which need to know about each other directly.
/// </summary>
public class AdaptationPipelineService
{
    private readonly IAdaptationTextGenerator _generator;
    private readonly ILogger<AdaptationPipelineService> _logger;

    public AdaptationPipelineService(IAdaptationTextGenerator generator, ILogger<AdaptationPipelineService> logger)
    {
        _generator = generator;
        _logger = logger;
    }

    public async Task<(List<AdaptedQuestion> Adapted, List<ValidationResult> Validation)> GenerateAndValidateAsync(
        Assessment assessment, AdaptationPlan plan, double? extractionConfidence, CancellationToken ct)
    {
        var questionsById = assessment.Sections
            .SelectMany(s => s.Questions)
            .ToDictionary(q => q.Id);

        var adapted = new List<AdaptedQuestion>();

        // Sequential, one call per question: keeps failures isolated to a single question and stays
        // friendly to the Gemini free tier's requests-per-minute limit (spec's design principle:
        // the AI only rewrites text for a plan the rules engine already resolved).
        foreach (var (questionId, resolvedRules) in plan.ResolvedRulesByQuestion)
        {
            if (!questionsById.TryGetValue(questionId, out var question))
                continue;

            var appliedInstructions = resolvedRules
                .Where(r => r.Applied)
                .Select(r => RuleCatalog.ById.TryGetValue(r.RuleId, out var rule)
                    ? new AppliedRuleInstruction(rule.Id, rule.Description, rule.Category.ToString(), rule.RequiresTeacherReview)
                    : null)
                .Where(instruction => instruction is not null)
                .Select(instruction => instruction!)
                .ToList();

            if (appliedInstructions.Count == 0 && !plan.IsCurricularChange)
            {
                // Nothing to change for this question at this level — keep the original text
                // verbatim rather than spending an AI call rewriting it into itself.
                adapted.Add(new AdaptedQuestion
                {
                    PlanId = plan.Id,
                    QuestionId = question.Id,
                    AdaptedText = question.OriginalText,
                    Points = question.Points,
                    ResponseMode = ResponseMode.Written
                });
                continue;
            }

            try
            {
                var request = new AdaptationTextRequest(
                    question, appliedInstructions, plan.Level, assessment.Language,
                    plan.IsCurricularChange, plan.CurricularObjective);

                var response = await _generator.GenerateAsync(request, ct);

                adapted.Add(new AdaptedQuestion
                {
                    PlanId = plan.Id,
                    QuestionId = question.Id,
                    AdaptedText = response.AdaptedText,
                    ResponseMode = response.ResponseMode,
                    Supports = response.Supports,
                    Points = question.Points, // always the original — never taken from the AI response
                    ChangeLog = response.ChangeLog
                });

                foreach (var warning in response.Warnings)
                    plan.Warnings.Add($"[{question.Id}] {warning}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Adaptation generation failed for question {QuestionId}", question.Id);
                plan.Warnings.Add($"[{question.Id}] HUMAN_REVIEW_REQUIRED: fallo generando la adaptación ({ex.Message}).");

                // Fail closed on the original text rather than dropping the question or guessing.
                adapted.Add(new AdaptedQuestion
                {
                    PlanId = plan.Id,
                    QuestionId = question.Id,
                    AdaptedText = question.OriginalText,
                    Points = question.Points,
                    ResponseMode = ResponseMode.Written
                });
            }
        }

        var validation = SafetyValidator.Validate(plan, assessment, questionsById, adapted, extractionConfidence);
        return (adapted, validation);
    }
}
