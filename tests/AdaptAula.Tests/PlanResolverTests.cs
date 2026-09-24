using AdaptAula.Domain;
using AdaptAula.RulesEngine;
using Xunit;

namespace AdaptAula.Tests;

public class PlanResolverTests
{
    private static Assessment SingleQuestionAssessment(params string[] constructTags)
    {
        var question = new Question { OriginalText = "¿Qué son los nutrientes?", Points = 3, ConstructTags = constructTags.ToList() };
        var section = new Section { Questions = new List<Question> { question } };
        question.SectionId = section.Id;
        var assessment = new Assessment { Title = "Test", TotalPoints = 3, Sections = new List<Section> { section } };
        section.AssessmentId = assessment.Id;
        return assessment;
    }

    private static Question OnlyQuestion(Assessment assessment) => assessment.Sections[0].Questions[0];

    [Fact]
    public void ConstructProtection_BlocksReadAloud_WhenQuestionTestsReading()
    {
        var assessment = SingleQuestionAssessment(ConstructTags.Reading);
        var profile = new StudentProfile { Alias = "alu-01", Measures = new List<string> { "dyslexia" } };

        var plan = PlanResolver.Resolve(new PlanResolverInput
        {
            Assessment = assessment, Profile = profile, Level = 2
        });

        var resolved = plan.ResolvedRulesByQuestion[OnlyQuestion(assessment).Id];
        var readAloud = resolved.Single(r => r.RuleId == "dyslexia.read_aloud_audio");

        Assert.False(readAloud.Applied);
        Assert.Equal(RuleSource.ConstructProtection, readAloud.Source);
    }

    [Fact]
    public void ConstructProtection_AllowsReadAloud_WhenReadingIsNotTheConstruct()
    {
        var assessment = SingleQuestionAssessment(); // no construct tags
        var profile = new StudentProfile { Alias = "alu-02", Measures = new List<string> { "dyslexia" } };

        var plan = PlanResolver.Resolve(new PlanResolverInput
        {
            Assessment = assessment, Profile = profile, Level = 2
        });

        var resolved = plan.ResolvedRulesByQuestion[OnlyQuestion(assessment).Id];
        var readAloud = resolved.Single(r => r.RuleId == "dyslexia.read_aloud_audio");

        Assert.True(readAloud.Applied);
    }

    [Fact]
    public void MinLevel_GatesRulesAboveThePlanLevel()
    {
        var assessment = SingleQuestionAssessment();
        var profile = new StudentProfile { Alias = "alu-03", Measures = new List<string> { "adhd" } };

        var plan = PlanResolver.Resolve(new PlanResolverInput
        {
            Assessment = assessment, Profile = profile, Level = 1 // progress_checklist needs level 2
        });

        var resolved = plan.ResolvedRulesByQuestion[OnlyQuestion(assessment).Id];
        var checklist = resolved.Single(r => r.RuleId == "adhd.progress_checklist");

        Assert.False(checklist.Applied);
    }

    [Fact]
    public void Exceptions_RemoveARuleThatThePresetWouldOtherwiseApply()
    {
        var assessment = SingleQuestionAssessment();
        var profile = new StudentProfile
        {
            Alias = "alu-04",
            Measures = new List<string> { "adhd" },
            Exceptions = new List<string> { "adhd.fragment_tasks" }
        };

        var plan = PlanResolver.Resolve(new PlanResolverInput
        {
            Assessment = assessment, Profile = profile, Level = 2
        });

        var resolved = plan.ResolvedRulesByQuestion[OnlyQuestion(assessment).Id];
        Assert.DoesNotContain(resolved, r => r.RuleId == "adhd.fragment_tasks");
    }

    [Fact]
    public void IndividualAccommodation_AppliesEvenWithoutItsPreset_AndIsTaggedAsIndividualMeasure()
    {
        var assessment = SingleQuestionAssessment();
        var profile = new StudentProfile
        {
            Alias = "alu-05",
            Measures = new List<string>(), // "gifted" preset not selected
            Accommodations = new List<string> { "gifted.optional_depth_extension" }
        };

        var plan = PlanResolver.Resolve(new PlanResolverInput
        {
            Assessment = assessment, Profile = profile, Level = 2
        });

        var resolved = plan.ResolvedRulesByQuestion[OnlyQuestion(assessment).Id];
        var extension = resolved.Single(r => r.RuleId == "gifted.optional_depth_extension");

        Assert.True(extension.Applied);
        Assert.Equal(RuleSource.IndividualMeasure, extension.Source);
    }

    [Fact]
    public void CurricularChange_NeverAutoTriggersFromAProfileAlone()
    {
        var assessment = SingleQuestionAssessment();
        // "curricular" preset selected, but the teacher has NOT authorized a curricular change.
        var profile = new StudentProfile { Alias = "alu-06", Measures = new List<string> { "curricular" } };

        var plan = PlanResolver.Resolve(new PlanResolverInput
        {
            Assessment = assessment, Profile = profile, Level = 3, CurricularChangeAuthorized = false
        });

        Assert.False(plan.IsCurricularChange);
    }

    [Fact]
    public void CurricularChange_ActivatesOnlyWithExplicitTeacherAuthorizationAndObjective()
    {
        var assessment = SingleQuestionAssessment();
        var profile = new StudentProfile { Alias = "alu-07", Measures = new List<string> { "curricular" } };

        var plan = PlanResolver.Resolve(new PlanResolverInput
        {
            Assessment = assessment,
            Profile = profile,
            Level = 3,
            CurricularChangeAuthorized = true,
            CurricularObjective = "Reconocer 3 grupos de nutrientes y una función de cada uno."
        });

        Assert.True(plan.IsCurricularChange);
        Assert.Equal(PlanStatus.NeedsTeacherReview, plan.Status);
    }

    [Fact]
    public void NeverApplyModeRule_DoesNotApplyFromPresetAlone_OnlyAsExplicitIndividualMeasure()
    {
        var assessment = SingleQuestionAssessment();
        var profileWithoutOverride = new StudentProfile { Alias = "alu-08", Measures = new List<string> { "spanish_l2" } };

        var plan = PlanResolver.Resolve(new PlanResolverInput
        {
            Assessment = assessment, Profile = profileWithoutOverride, Level = 2
        });

        var resolved = plan.ResolvedRulesByQuestion[OnlyQuestion(assessment).Id];
        var translation = resolved.Single(r => r.RuleId == "spanish_l2.translation_only_if_authorized");
        Assert.False(translation.Applied);
    }
}
