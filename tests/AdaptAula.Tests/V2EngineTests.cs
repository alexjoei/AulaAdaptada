using AdaptAula.Domain;
using AdaptAula.Infrastructure.Persistence;
using AdaptAula.RulesEngine;
using AdaptAula.Validation;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AdaptAula.Tests;

public class V2EngineTests
{
    private static (Assessment, Question) One(string text, string? answer = null, params string[] lockedFields)
    {
        var q = new Question { OriginalText = text, Points = 2, ExpectedAnswer = answer };
        var s = new Section { Questions = new List<Question> { q } };
        var a = new Assessment { TotalPoints = 2, Sections = new List<Section> { s }, LockedFields = lockedFields.ToList() };
        return (a, q);
    }

    private static List<ValidationResult> Validate(Assessment a, Question q, string adaptedText, IEnumerable<string>? supports = null, params string[] locks)
    {
        var plan = new AdaptationPlan { AssessmentId = a.Id, Locks = locks.ToList() };
        var adapted = new AdaptedQuestion { QuestionId = q.Id, AdaptedText = adaptedText, Points = q.Points, Supports = supports?.ToList() ?? new() };
        return SafetyValidator.Validate(plan, a, new Dictionary<Guid, Question> { [q.Id] = q }, new[] { adapted });
    }

    [Fact]
    public void Merge_RecommendedAgainstNotRecommended_IsFlaggedForTheTeacher_NotSilentlyResolved()
    {
        var (c, conflict) = MeasureSelection.Merge(new[] { MeasureClassification.Recommended, MeasureClassification.NotRecommended });
        Assert.True(conflict);
        Assert.Equal(MeasureClassification.RequiresTeacherDecision, c);

        Assert.Equal(MeasureClassification.Recommended, MeasureSelection.Merge(new[] { MeasureClassification.Optional, MeasureClassification.Recommended }).Classification);
    }

    [Fact]
    public void LegacyProfiles_KeepEveryPresetMeasureExceptTheNotRecommendedOnes()
    {
        var legacy = MeasureSelection.Compute(new[] { "dyslexia" }, Array.Empty<string>(), Array.Empty<string>(), schemaVersion: 1);

        Assert.True(legacy.Single(m => m.RuleId == "dyslexia.read_aloud_audio").Enabled);
        Assert.False(legacy.Single(m => m.RuleId == "language.simple_wording").Enabled);
    }

    [Fact]
    public void ExceptionBeatsAnIndividualAccommodation()
    {
        var m = MeasureSelection.Compute(new[] { "adhd" }, new[] { "time.extra_time" }, new[] { "time.extra_time" }).Single(x => x.RuleId == "time.extra_time");
        Assert.False(m.Enabled);
    }

    [Fact]
    public void StyleResolver_TakesTheMostAccessibleValue_AndThePerStudentOverride()
    {
        var rules = new[] { "dyslexia.typography_sans_serif", "low_vision.configurable_size_contrast", "present.line_spacing" }.Select(id => RuleCatalog.ById[id]);

        var style = DocumentStyleResolver.Resolve(rules, new Dictionary<string, string> { ["present.line_spacing.lineSpacing"] = "2" });

        Assert.Equal(18, style.FontSizePt);   // low-vision 18 beats dyslexia 14
        Assert.Equal(2, style.LineSpacing);   // the student's own value beats the default 1.5
        Assert.True(style.HighContrast);
    }

    [Fact]
    public void LogisticNotes_FillInTheStudentsOwnValues()
    {
        var notes = DocumentStyleResolver.LogisticNotes(new[] { RuleCatalog.ById["time.extra_time"] }, new Dictionary<string, string> { ["time.extra_time.extraTimePercent"] = "50" });
        Assert.Contains("50 %", notes.Single().Text);
    }

    [Fact]
    public void IndividualMeasure_BeatsAPreset_WhenTwoMeasuresConflict()
    {
        // two real measures made to conflict only for this test, via the resolver's public conflict logic
        var a = new AdaptationRule { Id = "x.preset", Description = "p", RiskLevel = RiskLevel.Low, ConflictsWith = new() { "x.individual" }, Kind = MeasureKind.Text };
        var b = new AdaptationRule { Id = "x.individual", Description = "i", RiskLevel = RiskLevel.High, ConflictsWith = new() { "x.preset" }, Kind = MeasureKind.Text };
        var (assessment, _) = One("Texto");
        var profile = new StudentProfile { Alias = "t", Accommodations = new() { "x.individual", "x.preset" } };

        var plan = PlanResolver.Resolve(new PlanResolverInput { Assessment = assessment, Profile = profile, Level = 2, ExtraRules = new[] { a, b } });
        var resolved = plan.ResolvedRulesByQuestion.Values.Single();

        Assert.Contains(plan.Warnings, w => w.StartsWith("UNSUPPORTED_CONFLICT"));
        Assert.Single(resolved, r => r.Applied);
    }

    [Fact]
    public void ConstructAlteringMeasures_AreMarkedProposalOnly_NeverApplied()
    {
        var (assessment, _) = One("Texto");
        var profile = new StudentProfile { Alias = "t", Measures = new() { "tdl_tel" } };

        var plan = PlanResolver.Resolve(new PlanResolverInput { Assessment = assessment, Profile = profile, Level = 2 });
        var rule = plan.ResolvedRulesByQuestion.Values.Single().Single(r => r.RuleId == "tdl_tel.direct_syntax");

        Assert.True(rule.Applied);
        Assert.True(rule.ProposalOnly);
    }

    [Fact]
    public void Validator_ProtectedVocabularyLanguageNumberingAndHints()
    {
        var (a, q) = One("1. Explica qué son los carbohidratos y las proteínas en la dieta de las personas.", "energía almacenada");
        a.ProtectedVocabulary.Add("carbohidratos");

        var codes = Validate(a, q, "7. Explain what the sugars and the proteins are in the diet of every person here.",
            new[] { "a", "b", "c", "d", "e" }, ProtectedElements.Language).Select(r => r.Code).ToList();

        Assert.Contains(ValidationCode.ProtectedVocabularyMissing, codes);
        Assert.Contains(ValidationCode.LanguageChanged, codes);
        Assert.Contains(ValidationCode.NumberingChanged, codes);
        Assert.Contains(ValidationCode.ExcessiveHints, codes);
    }

    [Fact]
    public void Validator_PartialAnswerLeak_IsCaughtEvenWhenNotAnExactMatch()
    {
        var (a, q) = One("Explica qué es la fotosíntesis.", "Proceso donde las plantas fabrican alimento usando luz solar");

        var results = Validate(a, q, "Explica qué es la fotosíntesis.", new[] { "Las plantas fabrican alimento usando luz solar" });

        Assert.Contains(results, r => r.Code == ValidationCode.HintRevealsAnswer);
    }

    [Fact]
    public void Validator_LockedCognitiveDemand_TurnsAMuchShorterTextIntoAnError()
    {
        var (a, q) = One("Compara la vida de los animales vertebrados con la de los invertebrados y justifica tu respuesta con tres ejemplos.");

        Assert.Contains(Validate(a, q, "Compara animales.", null, ProtectedElements.CognitiveDemand), r => r.Code == ValidationCode.CognitiveDemandChanged && r.Severity == ValidationSeverity.Error);
        Assert.DoesNotContain(Validate(a, q, "Compara animales.", null), r => r.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void Validator_DoesNotMistakeAShortNumericAnswerForAnotherLanguage()
    {
        var (a, q) = One("Calcula 25 + 17.");
        Assert.DoesNotContain(Validate(a, q, "Calcula 25 + 17 = ?", null, ProtectedElements.Language), r => r.Code == ValidationCode.LanguageChanged);
    }

    [Fact]
    public void Semaphore_IsGreenWhenSafe_OrangeForPendingProposals_RedForErrors()
    {
        var (a, q) = One("Explica la fotosíntesis de las plantas verdes.");
        var plan = new AdaptationPlan { AssessmentId = a.Id };
        AdaptationRule? Lookup(string id) => null;
        AdaptedQuestion Adapted(QuestionProposal? p = null) => new() { QuestionId = q.Id, AdaptedText = q.OriginalText, Points = 2, Proposal = p };

        Assert.Equal(SemaphoreLevel.Green, PedagogicalSemaphore.Compute(plan, new[] { Adapted() }, new List<ValidationResult>(), Lookup).Overall);
        Assert.Equal(SemaphoreLevel.Orange,
            PedagogicalSemaphore.Compute(plan, new[] { Adapted(new QuestionProposal { ProposedText = "x" }) }, new List<ValidationResult>(), Lookup).Overall);
        Assert.Equal(SemaphoreLevel.Red,
            PedagogicalSemaphore.Compute(plan, new[] { Adapted() },
                new List<ValidationResult> { new() { QuestionId = q.Id, Severity = ValidationSeverity.Error, Message = "x" } }, Lookup).Overall);
    }

    [Fact]
    public void Library_EveryNeedAndMeasureIsConsistent()
    {
        Assert.All(NecessityPresets.All, need => Assert.All(need.Measures, m => Assert.True(RuleCatalog.ById.ContainsKey(m.RuleId))));
        Assert.All(RuleCatalog.All.Where(r => r.Kind == MeasureKind.Style), r => Assert.NotEmpty(r.Style));
    }

    [Fact]
    public void SchemaUpgrader_AddsNewColumnsAndTables_ToADatabaseCreatedByTheOldVersion_KeepingItsData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"legacy-{Guid.NewGuid():N}.db");
        try
        {
            using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                // the V1 shape of two tables (no Settings/SchemaVersion, no ChangeLogRecords/ClassPacks…)
                command.CommandText = @"
                    CREATE TABLE ""StudentProfiles"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""Alias"" TEXT NOT NULL, ""Grade"" INTEGER NULL,
                        ""CurricularLevelOverride"" TEXT NULL, ""Measures"" TEXT NOT NULL, ""Accommodations"" TEXT NOT NULL, ""Exceptions"" TEXT NOT NULL, ""ReviewDate"" TEXT NOT NULL);
                    INSERT INTO ""StudentProfiles"" VALUES ('11111111-1111-1111-1111-111111111111','Alumno viejo',NULL,NULL,'[""dyslexia""]','[]','[]','2025-01-01 00:00:00');
                    CREATE TABLE ""AdaptationPlans"" (""Id"" TEXT NOT NULL PRIMARY KEY, ""AssessmentId"" TEXT NOT NULL, ""ProfileId"" TEXT NOT NULL, ""Level"" INTEGER NOT NULL,
                        ""Status"" INTEGER NOT NULL, ""IsCurricularChange"" INTEGER NOT NULL, ""CurricularObjective"" TEXT NULL, ""Locks"" TEXT NOT NULL,
                        ""ResolvedRulesByQuestion"" TEXT NOT NULL, ""Warnings"" TEXT NOT NULL, ""CreatedAt"" TEXT NOT NULL);";
                command.ExecuteNonQuery();
            }

            var options = new DbContextOptionsBuilder<AdaptAulaDbContext>().UseSqlite($"Data Source={path}").Options;
            using (var db = new AdaptAulaDbContext(options)) SchemaUpgrader.Upgrade(db);

            using (var db = new AdaptAulaDbContext(options))
            {
                var profile = db.StudentProfiles.Single();
                Assert.Equal("Alumno viejo", profile.Alias);
                Assert.Equal(0, profile.SchemaVersion);          // legacy => keeps its old "all preset measures apply" behaviour
                Assert.Empty(profile.Settings);
                Assert.Empty(db.ChangeLogRecords.ToList());       // new tables exist and are queryable
                Assert.Empty(db.ClassPacks.ToList());
                Assert.Empty(db.CustomMeasures.ToList());

                db.AdaptationPlans.Add(new AdaptationPlan { ProfileId = profile.Id, Style = new DocumentStyle { FontSizePt = 15 }, DocumentHtml = "<p>x</p>" });
                db.SaveChanges();
                Assert.Equal(15, db.AdaptationPlans.Single().Style.FontSizePt);
            }

            // idempotent: running it again changes nothing and doesn't throw
            using (var db = new AdaptAulaDbContext(options)) SchemaUpgrader.Upgrade(db);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
