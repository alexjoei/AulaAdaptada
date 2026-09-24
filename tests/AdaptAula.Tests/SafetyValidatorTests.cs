using AdaptAula.Domain;
using AdaptAula.Validation;
using Xunit;

namespace AdaptAula.Tests;

public class SafetyValidatorTests
{
    private static (Assessment Assessment, Question Question) BuildAssessment(
        string originalText, int points, string? expectedAnswer = null, params string[] constructTags)
    {
        var question = new Question
        {
            OriginalText = originalText,
            Points = points,
            ExpectedAnswer = expectedAnswer,
            ConstructTags = constructTags.ToList()
        };
        var section = new Section { Questions = new List<Question> { question } };
        question.SectionId = section.Id;
        var assessment = new Assessment { Title = "Test", TotalPoints = points, Sections = new List<Section> { section } };
        section.AssessmentId = assessment.Id;
        return (assessment, question);
    }

    private static AdaptationPlan EmptyPlan(Assessment assessment, Question question, params (string ruleId, bool applied)[] rules)
    {
        var plan = new AdaptationPlan { AssessmentId = assessment.Id };
        plan.ResolvedRulesByQuestion[question.Id] = rules
            .Select(r => new ResolvedRule { RuleId = r.ruleId, Applied = r.applied, Source = RuleSource.NecessityPreset })
            .ToList();
        return plan;
    }

    [Fact]
    public void PointsChanged_IsFlaggedAsError_WhenAdaptedPointsDifferFromOriginal()
    {
        var (assessment, question) = BuildAssessment("Explica la fotosíntesis.", 3);
        var plan = EmptyPlan(assessment, question);
        var adapted = new AdaptedQuestion { PlanId = plan.Id, QuestionId = question.Id, AdaptedText = "Explica la fotosíntesis.", Points = 5 };

        var results = SafetyValidator.Validate(plan, assessment, ByQuestionId(question), new List<AdaptedQuestion> { adapted });

        Assert.Contains(results, r => r.Code == ValidationCode.PointsChanged && r.Severity == ValidationSeverity.Error);
        Assert.False(SafetyValidator.CanExport(results));
    }

    [Fact]
    public void ContentDropped_IsFlaggedAsError_WhenAdaptedTextLosesMostKeywords()
    {
        var (assessment, question) = BuildAssessment(
            "Explica la diferencia entre carbohidratos y proteínas dando un ejemplo de alimento de cada uno.",
            2, constructTags: new[] { "reading" });
        var plan = EmptyPlan(assessment, question);
        var adapted = new AdaptedQuestion { PlanId = plan.Id, QuestionId = question.Id, AdaptedText = "Responde.", Points = 2 };

        var results = SafetyValidator.Validate(plan, assessment, ByQuestionId(question), new List<AdaptedQuestion> { adapted });

        Assert.Contains(results, r => r.Code == ValidationCode.ContentDropped && r.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void ReadingConstructBypassed_IsFlaggedAsError_WhenReadAloudRuleAppliedOnAReadingQuestion()
    {
        var (assessment, question) = BuildAssessment("Lee el texto y responde.", 3, constructTags: new[] { ConstructTags.Reading });
        var plan = EmptyPlan(assessment, question, ("dyslexia.read_aloud_audio", true));
        var adapted = new AdaptedQuestion { PlanId = plan.Id, QuestionId = question.Id, AdaptedText = "Lee el texto y responde.", Points = 3 };

        var results = SafetyValidator.Validate(plan, assessment, ByQuestionId(question), new List<AdaptedQuestion> { adapted });

        Assert.Contains(results, r => r.Code == ValidationCode.ReadingConstructBypassed && r.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void SpellingConstructBypassed_IsFlaggedAsError_WhenWordBankAppliedOnASpellingQuestion()
    {
        var (assessment, question) = BuildAssessment("Escribe correctamente las siguientes palabras.", 2, constructTags: new[] { ConstructTags.Spelling });
        var plan = EmptyPlan(assessment, question, ("dysorthography.word_bank_corrector", true));
        var adapted = new AdaptedQuestion { PlanId = plan.Id, QuestionId = question.Id, AdaptedText = "Escribe correctamente las siguientes palabras.", Points = 2 };

        var results = SafetyValidator.Validate(plan, assessment, ByQuestionId(question), new List<AdaptedQuestion> { adapted });

        Assert.Contains(results, r => r.Code == ValidationCode.SpellingConstructBypassed && r.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void HintRevealsAnswer_IsFlaggedAsError_WhenASupportContainsTheExpectedAnswer()
    {
        var (assessment, question) = BuildAssessment("¿Cuál es la capital de Francia?", 1, expectedAnswer: "París");
        var plan = EmptyPlan(assessment, question);
        var adapted = new AdaptedQuestion
        {
            PlanId = plan.Id,
            QuestionId = question.Id,
            AdaptedText = "¿Cuál es la capital de Francia?",
            Points = 1,
            Supports = new List<string> { "Pista: la respuesta es París." }
        };

        var results = SafetyValidator.Validate(plan, assessment, ByQuestionId(question), new List<AdaptedQuestion> { adapted });

        Assert.Contains(results, r => r.Code == ValidationCode.HintRevealsAnswer && r.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void DifficultyReduced_IsFlaggedAsWarningOnly_NotError()
    {
        var (assessment, question) = BuildAssessment(
            "Explica con tus propias palabras el ciclo del agua incluyendo evaporación condensación y precipitación.",
            3);
        var plan = EmptyPlan(assessment, question);
        var adapted = new AdaptedQuestion { PlanId = plan.Id, QuestionId = question.Id, AdaptedText = "Explica el ciclo.", Points = 3 };

        var results = SafetyValidator.Validate(plan, assessment, ByQuestionId(question), new List<AdaptedQuestion> { adapted });

        var warning = Assert.Single(results, r => r.Code == ValidationCode.DifficultyReduced);
        Assert.Equal(ValidationSeverity.Warning, warning.Severity);
        Assert.True(SafetyValidator.CanExport(results)); // warnings alone must not block export
    }

    [Fact]
    public void CurricularChange_AlwaysProducesAReviewEntry()
    {
        var (assessment, question) = BuildAssessment("Tarea 1", 2);
        var plan = EmptyPlan(assessment, question);
        plan.IsCurricularChange = true;
        var adapted = new AdaptedQuestion { PlanId = plan.Id, QuestionId = question.Id, AdaptedText = "Tarea 1", Points = 2 };

        var results = SafetyValidator.Validate(plan, assessment, ByQuestionId(question), new List<AdaptedQuestion> { adapted });

        var review = Assert.Single(results, r => r.Code == ValidationCode.CurricularChange);
        Assert.Equal(ValidationSeverity.Review, review.Severity);
        Assert.True(SafetyValidator.CanExport(results)); // review-only still exportable once approved by the UI flow
    }

    [Fact]
    public void LowExtractionConfidence_ProducesAReviewEntry_BelowThreshold()
    {
        var (assessment, question) = BuildAssessment("Tarea 1", 2);
        var plan = EmptyPlan(assessment, question);
        var adapted = new AdaptedQuestion { PlanId = plan.Id, QuestionId = question.Id, AdaptedText = "Tarea 1", Points = 2 };

        var results = SafetyValidator.Validate(plan, assessment, ByQuestionId(question), new List<AdaptedQuestion> { adapted }, extractionConfidence: 0.2);

        Assert.Contains(results, r => r.Code == ValidationCode.LowExtractionConfidence);
    }

    private static Dictionary<Guid, Question> ByQuestionId(Question question) => new() { [question.Id] = question };
}
