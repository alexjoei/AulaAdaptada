using AdaptAula.Api.Services;
using AdaptAula.Domain;
using AdaptAula.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AdaptAula.Tests;

/// <summary>Test double standing in for the real Gemini call — records how the pipeline grouped
/// questions into batches, and lets a test script drops/failures per question or per batch to
/// exercise the pipeline's fallback behavior without a live network call.</summary>
internal class FakeAdaptationTextGenerator : IAdaptationTextGenerator
{
    public List<int> BatchSizes { get; } = new();
    public HashSet<Guid> DropQuestionIds { get; } = new();
    public HashSet<Guid> ThrowForBatchesContaining { get; } = new();

    public Task<IReadOnlyList<AdaptationTextResponse>> GenerateBatchAsync(
        IReadOnlyList<AdaptationTextRequest> requests, CancellationToken ct = default)
    {
        BatchSizes.Add(requests.Count);

        if (requests.Any(r => ThrowForBatchesContaining.Contains(r.Question.Id)))
            throw new InvalidOperationException("simulated batch failure");

        IReadOnlyList<AdaptationTextResponse> responses = requests
            .Where(r => !DropQuestionIds.Contains(r.Question.Id))
            .Select(r => new AdaptationTextResponse(
                QuestionId: r.Question.Id,
                AdaptedText: $"adapted:{r.Question.OriginalText}",
                ResponseMode: ResponseMode.Written,
                Supports: new List<string>(),
                ChangeLog: new List<ChangeLogEntry>(),
                Warnings: new List<string>()))
            .ToList();

        return Task.FromResult(responses);
    }
}

public class AdaptationPipelineServiceTests
{
    private static (Assessment Assessment, AdaptationPlan Plan) BuildFixture(int questionCount, bool applyRuleToEach = true)
    {
        var sectionId = Guid.NewGuid();
        var questions = Enumerable.Range(0, questionCount)
            .Select(i => new Question { Id = Guid.NewGuid(), SectionId = sectionId, Order = i, OriginalText = $"Question {i}", Points = 1 })
            .ToList();

        var section = new Section { Id = sectionId, Order = 0, Questions = questions };
        var assessment = new Assessment { Language = "es", Sections = new List<Section> { section } };
        section.AssessmentId = assessment.Id;

        var plan = new AdaptationPlan { AssessmentId = assessment.Id, Level = 1 };
        foreach (var question in questions)
        {
            plan.ResolvedRulesByQuestion[question.Id] = applyRuleToEach
                ? new List<ResolvedRule> { new() { RuleId = "adhd.fragment_tasks", Source = RuleSource.NecessityPreset, Applied = true } }
                : new List<ResolvedRule>();
        }

        return (assessment, plan);
    }

    private static AdaptationPipelineService BuildPipeline(FakeAdaptationTextGenerator generator, GenerationProgressTracker tracker, int batchSize = 2) =>
        new(generator, tracker, Options.Create(new GeminiOptions { AdaptationBatchSize = batchSize }), NullLogger<AdaptationPipelineService>.Instance);

    [Fact]
    public async Task GenerateAndValidateAsync_ChunksQuestionsIntoBatchesOfTheConfiguredSize()
    {
        var (assessment, plan) = BuildFixture(questionCount: 5);
        var generator = new FakeAdaptationTextGenerator();
        var pipeline = BuildPipeline(generator, new GenerationProgressTracker(), batchSize: 2);

        var (adapted, _) = await pipeline.GenerateAndValidateAsync(assessment, plan, extractionConfidence: 1.0, CancellationToken.None);

        Assert.Equal(new[] { 2, 2, 1 }, generator.BatchSizes);
        Assert.Equal(5, adapted.Count);
        Assert.All(adapted, a => Assert.StartsWith("adapted:", a.AdaptedText));
    }

    [Fact]
    public async Task GenerateAndValidateAsync_SkipsTheAiCallEntirely_ForQuestionsWithNoAppliedRules()
    {
        var (assessment, plan) = BuildFixture(questionCount: 3, applyRuleToEach: false);
        var generator = new FakeAdaptationTextGenerator();
        var pipeline = BuildPipeline(generator, new GenerationProgressTracker());

        var (adapted, _) = await pipeline.GenerateAndValidateAsync(assessment, plan, extractionConfidence: 1.0, CancellationToken.None);

        Assert.Empty(generator.BatchSizes); // never called — nothing needed adapting
        Assert.Equal(3, adapted.Count);
        Assert.All(adapted, a => Assert.DoesNotContain("adapted:", a.AdaptedText)); // original text kept verbatim
    }

    [Fact]
    public async Task GenerateAndValidateAsync_FallsBackToOriginalText_WhenAQuestionIsMissingFromTheBatchResponse()
    {
        var (assessment, plan) = BuildFixture(questionCount: 3);
        var droppedQuestionId = assessment.Sections[0].Questions[1].Id;
        var generator = new FakeAdaptationTextGenerator { DropQuestionIds = { droppedQuestionId } };
        var pipeline = BuildPipeline(generator, new GenerationProgressTracker(), batchSize: 10);

        var (adapted, _) = await pipeline.GenerateAndValidateAsync(assessment, plan, extractionConfidence: 1.0, CancellationToken.None);

        var droppedResult = adapted.Single(a => a.QuestionId == droppedQuestionId);
        Assert.Equal("Question 1", droppedResult.AdaptedText); // fell back to the original, not lost
        Assert.Contains(plan.Warnings, w => w.Contains(droppedQuestionId.ToString()));
    }

    [Fact]
    public async Task GenerateAndValidateAsync_FallsBackWholeBatch_WhenTheBatchCallThrows()
    {
        var (assessment, plan) = BuildFixture(questionCount: 4);
        var failingQuestionId = assessment.Sections[0].Questions[0].Id;
        var generator = new FakeAdaptationTextGenerator { ThrowForBatchesContaining = { failingQuestionId } };
        var pipeline = BuildPipeline(generator, new GenerationProgressTracker(), batchSize: 2);

        var (adapted, _) = await pipeline.GenerateAndValidateAsync(assessment, plan, extractionConfidence: 1.0, CancellationToken.None);

        // The batch containing the failing question throws — both questions in THAT batch (size 2)
        // fail closed onto their original text; the other batch is unaffected.
        Assert.Equal(4, adapted.Count);
        var firstBatchQuestionIds = assessment.Sections[0].Questions.Take(2).Select(q => q.Id).ToHashSet();
        foreach (var result in adapted.Where(a => firstBatchQuestionIds.Contains(a.QuestionId)))
            Assert.DoesNotContain("adapted:", result.AdaptedText);
        Assert.Contains(plan.Warnings, w => w.Contains("HUMAN_REVIEW_REQUIRED"));
    }

    [Fact]
    public async Task GenerateAndValidateAsync_ClearsProgressTracker_AfterCompletion()
    {
        var (assessment, plan) = BuildFixture(questionCount: 3);
        var tracker = new GenerationProgressTracker();
        var pipeline = BuildPipeline(new FakeAdaptationTextGenerator(), tracker, batchSize: 10);

        await pipeline.GenerateAndValidateAsync(assessment, plan, extractionConfidence: 1.0, CancellationToken.None);

        Assert.Null(tracker.Get(plan.Id)); // no stale entry left once generation is done
    }
}
