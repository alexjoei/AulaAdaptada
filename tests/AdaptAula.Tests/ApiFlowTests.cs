using System.Net;
using System.Text.Json;
using Xunit;

namespace AdaptAula.Tests;

/// <summary>End-to-end flows against the real API (offline AI): what a teacher does in the browser, request by request.</summary>
public class ApiFlowTests : IClassFixture<ApiHarness>
{
    private readonly ApiHarness _api;

    public ApiFlowTests(ApiHarness api) => _api = api;

    private static string Id(JsonElement e) => e.GetProperty("id").GetString()!;

    // ------------------------------------------------------------------ V2 §1-3: library, needs, classification

    [Fact]
    public async Task Library_ExposesEveryRequiredNeedMeasureGroupAndClassification()
    {
        var library = await _api.Get("/api/measures");

        var needs = library.GetProperty("needs").EnumerateArray().Select(n => n.GetProperty("key").GetString()).ToList();
        foreach (var required in new[]
                 {
                     "adhd", "dyslexia", "dysgraphia", "dysorthography", "dyscalculia", "tdl_tel", "asd", "intellectual_disability",
                     "hearing_impairment", "low_vision", "motor_disability", "reading_comprehension", "slow_processing", "spanish_l2", "gifted", "custom"
                 })
            Assert.Contains(required, needs);

        var groups = library.GetProperty("groups").EnumerateArray().Select(g => g.GetProperty("key").GetString()).ToList();
        Assert.Equal(new[] { "presentation", "reading", "writing", "language", "attention", "math", "time", "response", "visual", "evaluation" }, groups);

        var classifications = library.GetProperty("needs").EnumerateArray()
            .SelectMany(n => n.GetProperty("measures").EnumerateArray()).Select(m => m.GetProperty("classification").GetString()).Distinct().ToList();
        Assert.Contains("Recommended", classifications);
        Assert.Contains("Optional", classifications);
        Assert.Contains("RequiresTeacherDecision", classifications);
        Assert.Contains("NotRecommended", classifications);
    }

    [Fact]
    public async Task EffectiveMeasures_MergesNeedsWithoutDuplicates_AndOnlyRecommendedStartOn()
    {
        var measures = await _api.Post("/api/profiles/effective-measures",
            new { needs = new[] { "adhd", "dyslexia", "slow_processing" }, accommodations = Array.Empty<string>(), exceptions = Array.Empty<string>() });

        var list = measures.EnumerateArray().ToList();
        Assert.Equal(list.Count, list.Select(m => m.GetProperty("ruleId").GetString()).Distinct().Count()); // no duplicates

        var extraTime = list.Single(m => m.GetProperty("ruleId").GetString() == "time.extra_time");
        Assert.Equal(3, extraTime.GetProperty("needKeys").GetArrayLength()); // shared by the three needs, listed once
        Assert.True(extraTime.GetProperty("enabled").GetBoolean());            // slow_processing recommends it

        var readAloud = list.Single(m => m.GetProperty("ruleId").GetString() == "dyslexia.read_aloud_audio");
        Assert.Equal("RequiresTeacherDecision", readAloud.GetProperty("classification").GetString());
        Assert.False(readAloud.GetProperty("enabled").GetBoolean()); // never on by itself

        var notRecommended = list.Single(m => m.GetProperty("ruleId").GetString() == "response.convert_to_multiple_choice");
        Assert.False(notRecommended.GetProperty("enabled").GetBoolean());
    }

    [Fact]
    public async Task CustomMeasure_CanBeCreated_UsedByAProfile_AndShowsUpInTheLibrary()
    {
        var created = await _api.Post("/api/measures/custom", new { description = "Subrayar en azul las preguntas con tablas", group = "Presentation", kind = "Text", altersAssessedConstruct = false });
        var customId = created.GetProperty("id").GetString()!;
        Assert.StartsWith("custom.", customId);

        var library = await _api.Get("/api/measures");
        Assert.Contains(library.GetProperty("measures").EnumerateArray(), m => m.GetProperty("id").GetString() == customId && m.GetProperty("isCustom").GetBoolean());

        var effective = await _api.Post("/api/profiles/effective-measures",
            new { needs = new[] { "custom" }, accommodations = new[] { customId }, exceptions = Array.Empty<string>() });
        var mine = effective.EnumerateArray().Single(m => m.GetProperty("ruleId").GetString() == customId);
        Assert.True(mine.GetProperty("enabled").GetBoolean());
        Assert.Equal("individual", mine.GetProperty("origin").GetString());
    }

    // ------------------------------------------------------------------ V2 §4: individual profiles

    [Fact]
    public async Task TwoStudentsWithTheSameNeed_CanHaveDifferentSettings_AndAreStoredByAliasOnly()
    {
        var a = await _api.CreateProfile("Alumno A", new[] { "dyslexia" }, settings: new Dictionary<string, string> { ["dyslexia.typography_sans_serif.fontSizePt"] = "16" });
        var b = await _api.CreateProfile("Alumno B", new[] { "dyslexia" }, exceptions: new[] { "dyslexia.left_align_low_density" });

        var assessment = await _api.CreateAssessment();
        var planA = await _api.CreateAndGeneratePlan(assessment, a);
        var planB = await _api.CreateAndGeneratePlan(assessment, b);

        Assert.Equal(16, planA.GetProperty("style").GetProperty("fontSizePt").GetDouble());
        Assert.Equal(14, planB.GetProperty("style").GetProperty("fontSizePt").GetDouble());

        var rulesB = planB.GetProperty("resolvedRulesByQuestion").EnumerateObject().First().Value.EnumerateArray().Select(r => r.GetProperty("ruleId").GetString()).ToList();
        Assert.DoesNotContain("dyslexia.left_align_low_density", rulesB);

        var stored = await _api.Get($"/api/profiles/{Id(a)}");
        Assert.Equal("Alumno A", stored.GetProperty("alias").GetString());
        Assert.False(stored.TryGetProperty("diagnosis", out _));
        Assert.False(stored.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task Profile_CanBeUpdated_Duplicated_AndRejectsAnEmptyOrHugeAlias()
    {
        var profile = await _api.CreateProfile("Alumno C", new[] { "adhd" });
        var updated = await _api.Put($"/api/profiles/{Id(profile)}", new
        {
            alias = "Alumno C2", measures = new[] { "adhd", "executive_functions" }, accommodations = new[] { "time.extra_time" },
            exceptions = Array.Empty<string>(), settings = new Dictionary<string, string> { ["time.extra_time.extraTimePercent"] = "50" }
        });
        Assert.Equal("Alumno C2", updated.GetProperty("alias").GetString());
        Assert.Equal("50", updated.GetProperty("settings").GetProperty("time.extra_time.extraTimePercent").GetString());

        var copy = await _api.Post($"/api/profiles/{Id(profile)}/duplicate?alias=Alumno%20D");
        Assert.Equal("Alumno D", copy.GetProperty("alias").GetString());
        Assert.NotEqual(Id(profile), Id(copy));

        await _api.Post("/api/profiles", new { alias = "  ", measures = new string[0], accommodations = new string[0], exceptions = new string[0] }, HttpStatusCode.BadRequest);
        await _api.Post("/api/profiles", new { alias = new string('x', 80), measures = new string[0], accommodations = new string[0], exceptions = new string[0] }, HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------ V2 §5, §14: levels and protected curricular adaptation

    [Fact]
    public async Task Level3_IsClampedToLevel2_UnlessTheTeacherTurnsOnTheCurricularSwitch()
    {
        var assessment = await _api.CreateAssessment();
        var profile = await _api.CreateProfile("Alumno E", new[] { "intellectual_disability" });

        var plain = await _api.Post($"/api/assessments/{Id(assessment)}/plans", new { profileId = Id(profile), level = 3 });
        Assert.Equal(2, plain.GetProperty("level").GetInt32());
        Assert.False(plain.GetProperty("isCurricularChange").GetBoolean());
        Assert.Contains(plain.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.StartsWith("NIVEL_LIMITADO"));

        var curricular = await _api.Post($"/api/assessments/{Id(assessment)}/plans", new
        {
            profileId = Id(profile), level = 3, curricularChangeAuthorized = true, curricularReference = "2º de Primaria",
            curricularObjective = "Sumar y restar hasta 100.", curricularCriteriaIds = new[] { "mat.c1.2.1" }
        });
        Assert.Equal(3, curricular.GetProperty("level").GetInt32());
        Assert.True(curricular.GetProperty("isCurricularChange").GetBoolean());
        Assert.Equal("2º de Primaria", curricular.GetProperty("curricularReference").GetString());
    }

    [Fact]
    public async Task CurricularSwitchWithoutReferents_DoesNotLowerAnything()
    {
        var assessment = await _api.CreateAssessment();
        var profile = await _api.CreateProfile("Alumno F", new[] { "intellectual_disability" });

        var plan = await _api.Post($"/api/assessments/{Id(assessment)}/plans", new { profileId = Id(profile), level = 3, curricularChangeAuthorized = true });

        Assert.False(plan.GetProperty("isCurricularChange").GetBoolean());
        Assert.Contains(plan.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.StartsWith("CURRICULAR_SIN_REFERENTES"));
    }

    // ------------------------------------------------------------------ V2 §6, §7, §13: LOMLOE base + analysis

    [Fact]
    public async Task Curriculum_HasTheFullStructure_IsFlaggedAsUnverified_AndKnowsNothingAboutNeeds()
    {
        var list = await _api.Get("/api/curriculum");
        var madrid = list.EnumerateArray().Single(c => c.GetProperty("id").GetString() == "madrid-primaria");
        Assert.Equal("Comunidad de Madrid", madrid.GetProperty("ccaa").GetString());
        Assert.False(madrid.GetProperty("verified").GetBoolean());
        Assert.Contains("verifica", madrid.GetProperty("note").GetString()!, StringComparison.OrdinalIgnoreCase);

        var full = await _api.Get("/api/curriculum/madrid-primaria");
        Assert.True(full.GetProperty("keyCompetences").GetArrayLength() >= 8);
        var math = full.GetProperty("areas").EnumerateArray().Single(a => a.GetProperty("id").GetString() == "mat");
        Assert.True(math.GetProperty("competences").GetArrayLength() >= 8);
        Assert.True(math.GetProperty("criteria").GetArrayLength() > 20);
        Assert.True(math.GetProperty("contents").GetArrayLength() > 20);

        // V2 §13: the curriculum is its own layer — no criterion/content is written "for" a diagnosis.
        var raw = full.GetRawText().ToLowerInvariant();
        foreach (var need in new[] { "dislexia", "tdah", "dyslexia", "adhd" })
            Assert.DoesNotContain(need, raw);
    }

    [Fact]
    public async Task Analyze_ProposesCriteriaOnlyFromTheCurriculum_AndTheTeacherCanConfirm()
    {
        var assessment = await _api.CreateAssessment(grade: 4, subject: "Matemáticas");
        await _api.Post($"/api/assessments/{Id(assessment)}/analyze", expect: HttpStatusCode.BadRequest); // no curriculum chosen yet

        await _api.Put($"/api/assessments/{Id(assessment)}", new { curriculumId = "madrid-primaria", curriculumAreaId = "mat" });
        var analyzed = await _api.Post($"/api/assessments/{Id(assessment)}/analyze");

        var questions = analyzed.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("questions").EnumerateArray()).ToList();
        Assert.All(questions, q =>
        {
            var analysis = q.GetProperty("analysis");
            Assert.False(string.IsNullOrWhiteSpace(analysis.GetProperty("cognitiveDemand").GetString()));
            Assert.Equal("ai", analysis.GetProperty("source").GetString());
            Assert.False(analysis.GetProperty("confirmed").GetBoolean());
            Assert.All(analysis.GetProperty("criteriaIds").EnumerateArray(), id => Assert.StartsWith("mat.c2.", id.GetString())); // grade 4 = cycle 2
        });

        var first = questions[0];
        var confirmed = await _api.Put($"/api/assessments/{Id(assessment)}", new
        {
            questions = new[] { new { id = Id(first), analysis = new { content = "Mi contenido", skill = "Mi habilidad", cognitiveDemand = "aplicar", criteriaIds = new[] { "mat.c2.2.1", "id-inventado" }, contentIds = new string[0], source = "teacher", confirmed = true } } }
        });
        var saved = confirmed.GetProperty("sections").EnumerateArray().SelectMany(s => s.GetProperty("questions").EnumerateArray()).Single(q => Id(q) == Id(first)).GetProperty("analysis");
        Assert.Equal("teacher", saved.GetProperty("source").GetString());
        Assert.Equal(new[] { "mat.c2.2.1" }, saved.GetProperty("criteriaIds").EnumerateArray().Select(c => c.GetString()).ToArray()); // invented id discarded
    }

    // ------------------------------------------------------------------ V2 §8: protected elements

    [Fact]
    public async Task ProtectedElements_ArePersisted_AndOnlyKnownKeysAreAccepted()
    {
        var assessment = await _api.CreateAssessment();
        Assert.Contains("question_count", assessment.GetProperty("lockedFields").EnumerateArray().Select(l => l.GetString()));

        var updated = await _api.Put($"/api/assessments/{Id(assessment)}", new
        {
            lockedFields = new[] { "content", "correct_answer", "essential_vocabulary", "campo_inventado" },
            protectedVocabulary = new[] { "carbohidratos", "  ", "proteínas" }
        });

        Assert.Equal(new[] { "content", "correct_answer", "essential_vocabulary" }, updated.GetProperty("lockedFields").EnumerateArray().Select(l => l.GetString()).ToArray());
        Assert.Equal(new[] { "carbohidratos", "proteínas" }, updated.GetProperty("protectedVocabulary").EnumerateArray().Select(l => l.GetString()).ToArray());
    }

    [Fact]
    public async Task ProtectedVocabulary_DeletedByTheTeacherInTheEditor_BlocksTheExport()
    {
        var assessment = await _api.CreateAssessment();
        await _api.Put($"/api/assessments/{Id(assessment)}", new { protectedVocabulary = new[] { "carbohidratos" } });
        var profile = await _api.CreateProfile("Alumno G", new[] { "adhd" });
        var plan = await _api.CreateAndGeneratePlan(assessment, profile);

        var document = await _api.Get($"/api/plans/{Id(plan)}/document");
        var html = document.GetProperty("html").GetString()!;
        Assert.Contains("carbohidratos", html, StringComparison.OrdinalIgnoreCase);

        var state = await _api.Put($"/api/plans/{Id(plan)}/document", new { html = html.Replace("carbohidratos", "azúcares", StringComparison.OrdinalIgnoreCase) });

        Assert.Contains(state.GetProperty("validationResults").EnumerateArray(), v => v.GetProperty("code").GetString() == "ProtectedVocabularyMissing" && v.GetProperty("severity").GetString() == "Error");
        Assert.False(state.GetProperty("canExport").GetBoolean());
        Assert.Equal("Red", state.GetProperty("semaphore").GetProperty("overall").GetString());

        await _api.PostForBytes($"/api/plans/{Id(plan)}/export?format=Pdf&approvedBy=Docente", null, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeletingAQuestionBlock_InTheEditor_IsCaughtAsAMissingQuestionAndLostPoints()
    {
        var assessment = await _api.CreateAssessment();
        var profile = await _api.CreateProfile("Alumno H", new[] { "adhd" });
        var plan = await _api.CreateAndGeneratePlan(assessment, profile);

        var html = (await _api.Get($"/api/plans/{Id(plan)}/document")).GetProperty("html").GetString()!;
        var start = html.LastIndexOf("<div data-type=\"question\"", StringComparison.Ordinal);
        var withoutLast = html[..start];

        var state = await _api.Put($"/api/plans/{Id(plan)}/document", new { html = withoutLast });
        var codes = state.GetProperty("validationResults").EnumerateArray().Select(v => v.GetProperty("code").GetString()).ToList();

        Assert.Contains("QuestionMissing", codes);
        Assert.Contains("QuestionCountChanged", codes);
        Assert.Contains("PointsChanged", codes);
        Assert.False(state.GetProperty("canExport").GetBoolean());
    }

    // ------------------------------------------------------------------ V2 §9: the document the teacher edits

    [Fact]
    public async Task EditedDocument_IsWhatGetsExported_WithTheTeachersFormatting()
    {
        var assessment = await _api.CreateAssessment();
        var profile = await _api.CreateProfile("Alumno I", new[] { "dyslexia" });
        var plan = await _api.CreateAndGeneratePlan(assessment, profile);

        var document = await _api.Get($"/api/plans/{Id(plan)}/document");
        Assert.False(document.GetProperty("edited").GetBoolean());
        var html = document.GetProperty("html").GetString()!;
        var edited = html.Replace("<h1>", "<p style=\"text-align:center\"><span style=\"color:#cc0000;font-size:22pt\"><strong>MARCA-DEL-DOCENTE</strong></span></p><h1>");

        var style = document.GetProperty("style");
        var newStyle = JsonSerializer.Deserialize<Dictionary<string, object>>(style.GetRawText())!;
        newStyle["fontSizePt"] = 17;
        var state = await _api.Put($"/api/plans/{Id(plan)}/document", new { html = edited, style = newStyle });
        Assert.True(state.GetProperty("canExport").GetBoolean());

        var reread = await _api.Get($"/api/plans/{Id(plan)}/document");
        Assert.True(reread.GetProperty("edited").GetBoolean());
        Assert.Contains("MARCA-DEL-DOCENTE", reread.GetProperty("html").GetString());
        Assert.Equal(17, reread.GetProperty("style").GetProperty("fontSizePt").GetDouble());

        var docx = await _api.PostForBytes($"/api/plans/{Id(plan)}/export?format=Docx&approvedBy=Docente", null);
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(docx));
        using var reader = new StreamReader(archive.GetEntry("word/document.xml")!.Open());
        var xml = await reader.ReadToEndAsync();
        Assert.Contains("MARCA-DEL-DOCENTE", xml);
        Assert.Contains("CC0000", xml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("w:val=\"34\"", xml); // 17pt = 34 half-points, the size the teacher chose

        var pdf = await _api.PostForBytes($"/api/plans/{Id(plan)}/export?format=Pdf&approvedBy=Docente", null);
        Assert.StartsWith("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));

        // resetting throws the manual edits away and goes back to the generated document
        var reset = await _api.Post($"/api/plans/{Id(plan)}/document/reset");
        Assert.True(reset.GetProperty("canExport").GetBoolean());
        Assert.DoesNotContain("MARCA-DEL-DOCENTE", (await _api.Get($"/api/plans/{Id(plan)}/document")).GetProperty("html").GetString());
    }

    // ------------------------------------------------------------------ V2 §15, §16, §21, §23: comparator, semaphore, history, proposals

    [Fact]
    public async Task Generation_MarksChangesWithTheMeasureThatCausedThem_AndKeepsPointsAndTotals()
    {
        var assessment = await _api.CreateAssessment();
        var profile = await _api.CreateProfile("Alumno J", new[] { "adhd" });
        var plan = await _api.CreateAndGeneratePlan(assessment, profile);
        var state = await _api.Get($"/api/plans/{Id(plan)}/state");

        var adapted = state.GetProperty("adaptedQuestions").EnumerateArray().ToList();
        Assert.Equal(3, adapted.Count);
        Assert.Equal(10, adapted.Sum(a => a.GetProperty("points").GetInt32()));
        Assert.Contains(adapted.SelectMany(a => a.GetProperty("changeLog").EnumerateArray()), c => c.GetProperty("ruleId").GetString() == "adhd.highlight_action_verbs");

        var measures = await _api.Get($"/api/plans/{Id(plan)}/measures");
        var why = measures.EnumerateArray().Single(m => m.GetProperty("id").GetString() == "adhd.highlight_action_verbs");
        Assert.False(string.IsNullOrWhiteSpace(why.GetProperty("rationale").GetString())); // "por qué" shown in the comparator

        var semaphore = state.GetProperty("semaphore");
        Assert.Equal(3, semaphore.GetProperty("questions").GetArrayLength());
    }

    [Fact]
    public async Task UndoingOneChange_RestoresTheTextAndIsWrittenToTheHistory()
    {
        var assessment = await _api.CreateAssessment();
        var profile = await _api.CreateProfile("Alumno K", new[] { "adhd" });
        var plan = await _api.CreateAndGeneratePlan(assessment, profile);
        var state = await _api.Get($"/api/plans/{Id(plan)}/state");

        var first = state.GetProperty("adaptedQuestions").EnumerateArray().First(a => a.GetProperty("adaptedText").GetString()!.Contains("**"));
        var bolded = first.GetProperty("adaptedText").GetString()!;

        var after = await _api.Put($"/api/plans/{Id(plan)}/adapted-questions/{Id(first)}",
            new { adaptedText = bolded.Replace("**", ""), kind = "undo", ruleId = "adhd.highlight_action_verbs" });
        var updated = after.GetProperty("adaptedQuestions").EnumerateArray().Single(a => Id(a) == Id(first));
        Assert.DoesNotContain("**", updated.GetProperty("adaptedText").GetString());

        var history = await _api.Get($"/api/plans/{Id(plan)}/changelog");
        var undo = history.EnumerateArray().Single(h => h.GetProperty("kind").GetString() == "undo");
        Assert.Equal("adhd.highlight_action_verbs", undo.GetProperty("ruleId").GetString());
        Assert.Equal("Rejected", undo.GetProperty("decision").GetString());
        Assert.Equal(bolded, undo.GetProperty("before").GetString());
        Assert.NotEqual(DateTime.MinValue, undo.GetProperty("at").GetDateTime());
    }

    [Fact]
    public async Task ConstructAlteringMeasure_IsOnlyProposed_NeverAppliedSilently_AndAcceptingItIsLogged()
    {
        var assessment = await _api.CreateAssessment();
        var profile = await _api.CreateProfile("Alumno L", new[] { "tdl_tel" }); // tdl_tel.direct_syntax alters what is assessed
        var plan = await _api.CreateAndGeneratePlan(assessment, profile);
        var state = await _api.Get($"/api/plans/{Id(plan)}/state");

        var withProposal = state.GetProperty("adaptedQuestions").EnumerateArray().First(a => a.GetProperty("proposal").ValueKind == JsonValueKind.Object);
        var proposal = withProposal.GetProperty("proposal");
        var text = withProposal.GetProperty("adaptedText").GetString();
        Assert.Equal("Pending", proposal.GetProperty("status").GetString());
        Assert.NotEqual(text, proposal.GetProperty("proposedText").GetString()); // not applied to the text
        Assert.Contains(state.GetProperty("validationResults").EnumerateArray(), v => v.GetProperty("code").GetString() == "ProposalPending");
        Assert.NotEqual("Green", state.GetProperty("semaphore").GetProperty("questions").EnumerateArray().Single(q => q.GetProperty("questionId").GetString() == withProposal.GetProperty("questionId").GetString()).GetProperty("level").GetString());

        var accepted = await _api.Post($"/api/plans/{Id(plan)}/adapted-questions/{Id(withProposal)}/proposal", new { accept = true });
        var now = accepted.GetProperty("adaptedQuestions").EnumerateArray().Single(a => Id(a) == Id(withProposal));
        Assert.Equal(proposal.GetProperty("proposedText").GetString(), now.GetProperty("adaptedText").GetString());
        Assert.Equal("Accepted", now.GetProperty("proposal").GetProperty("status").GetString());

        var history = await _api.Get($"/api/plans/{Id(plan)}/changelog");
        Assert.Contains(history.EnumerateArray(), h => h.GetProperty("kind").GetString() == "proposal" && h.GetProperty("decision").GetString() == "Accepted");
    }

    [Fact]
    public async Task RejectingAProposal_KeepsTheTextAndClearsTheReviewFlag()
    {
        var assessment = await _api.CreateAssessment();
        var profile = await _api.CreateProfile("Alumno M", new[] { "tdl_tel" });
        var plan = await _api.CreateAndGeneratePlan(assessment, profile);
        var state = await _api.Get($"/api/plans/{Id(plan)}/state");
        var withProposal = state.GetProperty("adaptedQuestions").EnumerateArray().First(a => a.GetProperty("proposal").ValueKind == JsonValueKind.Object);

        var rejected = await _api.Post($"/api/plans/{Id(plan)}/adapted-questions/{Id(withProposal)}/proposal", new { accept = false });

        var now = rejected.GetProperty("adaptedQuestions").EnumerateArray().Single(a => Id(a) == Id(withProposal));
        Assert.Equal(withProposal.GetProperty("adaptedText").GetString(), now.GetProperty("adaptedText").GetString());
        Assert.Equal("Rejected", now.GetProperty("proposal").GetProperty("status").GetString());
    }

    // ------------------------------------------------------------------ V2 §10: AI tools on a text

    [Fact]
    public async Task TextTool_ReturnsOriginalAndProposal_AndWarnsWhenAHintLeaksTheExpectedAnswer()
    {
        var assessment = await _api.CreateAssessment("1. Escribe el nombre del gas que respiran las plantas para fabricar su alimento. (2 puntos)");
        var question = assessment.GetProperty("sections")[0].GetProperty("questions")[0];
        await _api.Put($"/api/assessments/{Id(assessment)}", new { questions = new[] { new { id = Id(question), expectedAnswer = "alimento", points = 2 } } });

        var result = await _api.Post("/api/ai/text-tool", new { tool = "Hint", text = "Escribe el nombre del gas que respiran las plantas.", questionId = Id(question) });

        Assert.Equal("Escribe el nombre del gas que respiran las plantas.", result.GetProperty("original").GetString());
        Assert.NotEqual(result.GetProperty("original").GetString(), result.GetProperty("proposal").GetString());
        var warnings = result.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToList();
        Assert.Contains(warnings, w => w!.Contains("puede facilitar", StringComparison.OrdinalIgnoreCase)); // hints always warn they may lower demand

        // a word bank built from the question's own words that happens to contain the answer is caught
        var leak = await _api.Post("/api/ai/text-tool", new { tool = "WordBank", text = "alimento gas plantas", questionId = Id(question) });
        Assert.Contains(leak.GetProperty("warnings").EnumerateArray(), w => w.GetString()!.Contains("respuesta esperada"));
    }

    [Theory]
    [InlineData("Shorten")]
    [InlineData("SimplifySyntax")]
    [InlineData("SplitIntoSteps")]
    [InlineData("HighlightKeywords")]
    [InlineData("AddExample")]
    [InlineData("WordBank")]
    [InlineData("Hint")]
    [InlineData("Checklist")]
    [InlineData("Organizer")]
    [InlineData("ReadAloud")]
    public async Task EveryTextTool_Works(string tool)
    {
        var result = await _api.Post("/api/ai/text-tool", new { tool, text = "Explica la diferencia entre carbohidratos y proteínas. Da un ejemplo de cada uno." });

        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("proposal").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(result.GetProperty("note").GetString()));
    }

    [Fact]
    public async Task TextTool_RejectsEmptyOrHugeSelections()
    {
        await _api.Post("/api/ai/text-tool", new { tool = "Shorten", text = "   " }, HttpStatusCode.BadRequest);
        await _api.Post("/api/ai/text-tool", new { tool = "Shorten", text = new string('a', 5000) }, HttpStatusCode.BadRequest);
    }

    // ------------------------------------------------------------------ V2 §19, §20: class pack and exports

    [Fact]
    public async Task ClassPack_GeneratesEveryStudent_AndExportsAZipWithOriginalVersionsAndSheets()
    {
        var assessment = await _api.CreateAssessment();
        var a = await _api.CreateProfile("Alumno A", new[] { "dyslexia" });
        var b = await _api.CreateProfile("Alumno B", new[] { "adhd" });
        var c = await _api.CreateProfile("Alumno C", new[] { "asd", "slow_processing" });
        await _api.Put($"/api/assessments/{Id(assessment)}", new { questions = new[] { new { id = Id(assessment.GetProperty("sections")[0].GetProperty("questions")[0]), expectedAnswer = "Los carbohidratos dan energía", points = 3 } } });

        var pack = await _api.Post($"/api/assessments/{Id(assessment)}/packs", new { title = "Ciencias 4ºA", items = new[] { new { profileId = Id(a), level = 2 }, new { profileId = Id(b), level = 2 }, new { profileId = Id(c), level = 2 } } });
        Assert.Equal(3, pack.GetProperty("plans").GetArrayLength());
        Assert.All(pack.GetProperty("plans").EnumerateArray(), p => Assert.False(p.GetProperty("generated").GetBoolean()));

        // exporting before generating is refused, naming the student
        var early = await _api.Post($"/api/packs/{Id(pack)}/export", new { }, HttpStatusCode.Conflict);
        Assert.Contains("Alumno A", early.GetProperty("error").GetString());

        foreach (var plan in pack.GetProperty("plans").EnumerateArray())
            await _api.Post($"/api/plans/{plan.GetProperty("planId").GetString()}/generate");

        var described = await _api.Get($"/api/packs/{Id(pack)}");
        Assert.All(described.GetProperty("plans").EnumerateArray(), p => Assert.True(p.GetProperty("generated").GetBoolean()));

        var zip = await _api.PostForBytes($"/api/packs/{Id(pack)}/export",
            new { pdf = true, docx = true, includeOriginal = true, teacherSheet = true, answerKey = true, changeLog = true, accessibleHtml = true, approvedBy = "Docente" });
        var entries = ApiHarness.ZipEntries(zip);

        Assert.Contains(entries, e => e.StartsWith("01_Original_") && e.EndsWith(".pdf"));
        Assert.Contains(entries, e => e.StartsWith("01_Original_") && e.EndsWith(".docx"));
        foreach (var alias in new[] { "Alumno_A", "Alumno_B", "Alumno_C" })
        {
            Assert.Contains(entries, e => e.Contains(alias) && e.EndsWith(".pdf") && !e.StartsWith("Historial"));
            Assert.Contains(entries, e => e.Contains(alias) && e.EndsWith(".docx") && !e.StartsWith("Historial"));
            Assert.Contains(entries, e => e.Contains(alias) && e.EndsWith("_version_digital.html"));
            Assert.Contains(entries, e => e.StartsWith("Historial_" + alias));
        }
        Assert.Contains("Hoja_docente.pdf", entries);
        Assert.Contains("Hoja_docente.docx", entries);
        Assert.Contains("Rubrica_solucionario.pdf", entries);

        // the accessible version is a real, semantic page
        using var archive = new System.IO.Compression.ZipArchive(new MemoryStream(zip));
        var digital = archive.Entries.First(e => e.FullName.EndsWith("_version_digital.html"));
        using var reader = new StreamReader(digital.Open());
        var html = await reader.ReadToEndAsync();
        Assert.Contains("<html lang=\"es\">", html);
        Assert.Contains("<main", html);
        Assert.Contains("Saltar al contenido", html);
    }

    [Fact]
    public async Task ClassPack_RefusesDuplicatedStudentsAndEmptyPacks()
    {
        var assessment = await _api.CreateAssessment();
        var a = await _api.CreateProfile("Alumno X", new[] { "dyslexia" });

        await _api.Post($"/api/assessments/{Id(assessment)}/packs", new { items = new object[0] }, HttpStatusCode.BadRequest);
        await _api.Post($"/api/assessments/{Id(assessment)}/packs", new { items = new[] { new { profileId = Id(a), level = 2 }, new { profileId = Id(a), level = 2 } } }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SingleStudentBundle_IncludesOnlyTheChosenExtras()
    {
        var assessment = await _api.CreateAssessment();
        var profile = await _api.CreateProfile("Alumno N", new[] { "dyslexia" });
        var plan = await _api.CreateAndGeneratePlan(assessment, profile);

        var zip = await _api.PostForBytes($"/api/plans/{Id(plan)}/export-bundle",
            new { pdf = true, docx = false, teacherSheet = false, answerKey = true, changeLog = false, accessibleHtml = false });
        var entries = ApiHarness.ZipEntries(zip);

        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.EndsWith(".pdf") && e.Contains("Alumno_N"));
        Assert.Contains("Rubrica_solucionario.pdf", entries);
        Assert.DoesNotContain(entries, e => e.Contains("Original")); // single-student bundle never carries the original
    }

    [Fact]
    public async Task Export_RequiresAnApproverWhenSomethingNeedsReview_AndRecordsTheVersion()
    {
        var assessment = await _api.CreateAssessment();
        var profile = await _api.CreateProfile("Alumno O", new[] { "tdl_tel" }); // leaves a pending proposal = a review item
        var plan = await _api.CreateAndGeneratePlan(assessment, profile);

        var blocked = await _api.PostForBytes($"/api/plans/{Id(plan)}/export?format=Docx", null, HttpStatusCode.Conflict);
        Assert.Contains("aprueba", System.Text.Encoding.UTF8.GetString(blocked), StringComparison.OrdinalIgnoreCase);

        var ok = await _api.PostForBytes($"/api/plans/{Id(plan)}/export?format=Docx&approvedBy=Maria", null);
        Assert.True(ok.Length > 1000);

        var history = await _api.Get($"/api/plans/{Id(plan)}/changelog");
        Assert.Contains(history.EnumerateArray(), h => h.GetProperty("kind").GetString() == "export" && h.GetProperty("actor").GetString() == "Maria");
    }

    // ------------------------------------------------------------------ V2 §24: privacy

    [Fact]
    public async Task DeletingAnAssessment_RemovesEverythingGeneratedFromIt()
    {
        var assessment = await _api.CreateAssessment();
        var profile = await _api.CreateProfile("Alumno P", new[] { "adhd" });
        var plan = await _api.CreateAndGeneratePlan(assessment, profile);
        await _api.PostForBytes($"/api/plans/{Id(plan)}/export?format=Pdf&approvedBy=Docente", null);

        await _api.Send(HttpMethod.Delete, $"/api/assessments/{Id(assessment)}", null, HttpStatusCode.NoContent);

        await _api.Get($"/api/assessments/{Id(assessment)}", HttpStatusCode.NotFound);
        await _api.Get($"/api/plans/{Id(plan)}/state", HttpStatusCode.NotFound);
        Assert.Empty((await _api.Get($"/api/plans/{Id(plan)}/changelog")).EnumerateArray());
        await _api.Get($"/api/profiles/{Id(profile)}"); // the student profile (alias + measures) is separate and survives
    }

    [Fact]
    public async Task DeletingAProfile_AlsoDeletesTheAdaptationsMadeForThatStudent()
    {
        var assessment = await _api.CreateAssessment();
        var profile = await _api.CreateProfile("Alumno Q", new[] { "adhd" });
        var plan = await _api.CreateAndGeneratePlan(assessment, profile);

        await _api.Send(HttpMethod.Delete, $"/api/profiles/{Id(profile)}", null, HttpStatusCode.NoContent);

        await _api.Get($"/api/plans/{Id(plan)}", HttpStatusCode.NotFound);
        await _api.Get($"/api/assessments/{Id(assessment)}"); // the test itself stays
    }

    // ------------------------------------------------------------------ V2 §22: rules live in data

    [Fact]
    public async Task Measures_AreDataDriven_EveryRuleReferencedByANeedExists()
    {
        var library = await _api.Get("/api/measures");
        var ids = library.GetProperty("measures").EnumerateArray().Select(m => m.GetProperty("id").GetString()).ToHashSet();

        foreach (var need in library.GetProperty("needs").EnumerateArray())
            foreach (var measure in need.GetProperty("measures").EnumerateArray())
                Assert.Contains(measure.GetProperty("ruleId").GetString(), ids);

        Assert.True(ids.Count >= 80);
    }
}
