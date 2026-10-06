using AdaptAula.Domain;
using AdaptAula.Infrastructure.Export;
using AdaptAula.Infrastructure.Persistence;
using AdaptAula.RulesEngine;
using AdaptAula.Validation;
using Microsoft.EntityFrameworkCore;

namespace AdaptAula.Api.Services;

/// <summary>What the editor's HTML says about one question (V2 §9, §17): used to validate the document the teacher actually
/// edited, not just the AI's draft.</summary>
public record DocumentQuestion(Guid QuestionId, int? Points, string Text, List<string> Supports, bool HasAnswerSpace, List<string> ImageRoles);

public record PlanAssessment(
    AdaptationPlan Plan, Assessment Assessment, List<AdaptedQuestion> Adapted, Dictionary<string, AdaptationRule> ExtraRules);

/// <summary>
/// Everything that keeps the adapted questions, the editable document and the validation results consistent: builds the
/// document, validates the edited version, and computes the pedagogical traffic light.
/// </summary>
public class PlanDocumentService
{
    private readonly AdaptAulaDbContext _db;

    public PlanDocumentService(AdaptAulaDbContext db) => _db = db;

    public async Task<Dictionary<string, AdaptationRule>> LoadExtraRulesAsync(CancellationToken ct) =>
        (await _db.CustomMeasures.ToListAsync(ct)).Select(CustomMeasureFactory.ToRule).ToDictionary(r => r.Id);

    public async Task<PlanAssessment?> LoadAsync(Guid planId, CancellationToken ct)
    {
        var plan = await _db.AdaptationPlans.FindAsync(new object[] { planId }, ct);
        if (plan is null) return null;
        var assessment = await _db.Assessments.Include(a => a.Sections).ThenInclude(s => s.Questions)
            .FirstOrDefaultAsync(a => a.Id == plan.AssessmentId, ct);
        if (assessment is null) return null;
        var adapted = await _db.AdaptedQuestions.Where(a => a.PlanId == planId).ToListAsync(ct);
        return new PlanAssessment(plan, assessment, adapted, await LoadExtraRulesAsync(ct));
    }

    public static Dictionary<Guid, Question> QuestionsById(Assessment assessment) =>
        assessment.Sections.SelectMany(s => s.Questions).ToDictionary(q => q.Id);

    /// <summary>The document HTML to show/export: the teacher's edited version when there is one, otherwise a fresh one.</summary>
    public static string CurrentHtml(PlanAssessment ctx) =>
        ctx.Plan.DocumentHtml ?? DocumentHtmlBuilder.BuildAdapted(ctx.Assessment, ctx.Plan, QuestionsById(ctx.Assessment), ctx.Adapted);

    /// <summary>Rebuilds the document from the adapted questions — only while the teacher hasn't edited it by hand,
    /// so a comparator change never overwrites manual edits.</summary>
    public static bool RegenerateDocumentIfUnedited(PlanAssessment ctx)
    {
        if (ctx.Plan.DocumentEditedAt is not null) return false;
        ctx.Plan.DocumentHtml = DocumentHtmlBuilder.BuildAdapted(ctx.Assessment, ctx.Plan, QuestionsById(ctx.Assessment), ctx.Adapted);
        return true;
    }

    public static List<DocumentQuestion> ExtractQuestions(string? html)
    {
        var result = new List<DocumentQuestion>();
        foreach (var box in Flatten(RichDocumentParser.Parse(html)).OfType<BoxBlock>().Where(b => b.Kind == "question" && b.QuestionId is not null))
        {
            var text = new List<string>();
            var supports = new List<string>();
            var roles = new List<string>();
            var hasSpace = false;

            foreach (var child in Flatten(box.Children))
            {
                switch (child)
                {
                    case ParagraphBlock p when p.HeadingLevel == 0:
                        var plain = p.PlainText.Trim();
                        if (plain.Length == 0) break;
                        if (plain.StartsWith("• ") || plain.StartsWith("□ ") || plain.StartsWith("☐ ")) supports.Add(plain[2..].Trim());
                        else text.Add(plain);
                        break;
                    case AnswerSpaceBlock { Lines: > 0 }:
                        hasSpace = true;
                        break;
                    case ImageDocBlock image when image.Role is not null:
                        roles.Add(image.Role);
                        break;
                }
            }

            result.Add(new DocumentQuestion(box.QuestionId!.Value, box.Points, string.Join("\n", text), supports, hasSpace, roles));
        }
        return result;
    }

    private static IEnumerable<DocBlock> Flatten(IEnumerable<DocBlock> blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;
            if (block is BoxBlock box && box.Kind != "question")
                foreach (var inner in Flatten(box.Children)) yield return inner;
            else if (block is TableBlock table)
                foreach (var inner in Flatten(table.Rows.SelectMany(r => r).SelectMany(cell => cell))) yield return inner;
        }
    }

    /// <summary>The adapted questions as they stand now: from the edited document when the teacher edited it, otherwise as generated.</summary>
    public static List<AdaptedQuestion> EffectiveAdapted(PlanAssessment ctx, out Dictionary<Guid, DocumentQuestion> extracted)
    {
        extracted = new Dictionary<Guid, DocumentQuestion>();
        if (ctx.Plan.DocumentEditedAt is null || ctx.Plan.DocumentHtml is null) return ctx.Adapted;

        foreach (var q in ExtractQuestions(ctx.Plan.DocumentHtml)) extracted[q.QuestionId] = q;
        var questions = QuestionsById(ctx.Assessment);

        var effective = new List<AdaptedQuestion>();
        foreach (var (id, e) in extracted)
        {
            if (!questions.TryGetValue(id, out var original)) continue;
            var stored = ctx.Adapted.FirstOrDefault(a => a.QuestionId == id);
            effective.Add(new AdaptedQuestion
            {
                Id = stored?.Id ?? Guid.NewGuid(),
                PlanId = ctx.Plan.Id,
                QuestionId = id,
                AdaptedText = e.Text,
                Supports = e.Supports,
                // Points travel with the block; a tampered/removed attribute is reported by the validator instead of being silently fixed.
                Points = e.Points ?? 0,
                ResponseMode = stored?.ResponseMode ?? ResponseMode.Written,
                ChangeLog = stored?.ChangeLog ?? new List<ChangeLogEntry>(),
                Proposal = stored?.Proposal,
                TeacherApproved = stored?.TeacherApproved ?? false
            });
        }
        return effective;
    }

    public (List<ValidationResult> Validation, SemaphoreReport Semaphore) Validate(PlanAssessment ctx)
    {
        var effective = EffectiveAdapted(ctx, out var extracted);
        var questions = QuestionsById(ctx.Assessment);

        var validation = SafetyValidator.Validate(ctx.Plan, ctx.Assessment, questions, effective, ctx.Assessment.ExtractionConfidence);

        foreach (var adapted in effective)
        {
            if (!questions.TryGetValue(adapted.QuestionId, out var original)) continue;

            if (extracted.TryGetValue(adapted.QuestionId, out var e))
            {
                var needsSpace = original.Type is QuestionType.OpenText or QuestionType.ShortAnswer or QuestionType.FillInTheBlank or QuestionType.Classification;
                if (needsSpace && !e.HasAnswerSpace && adapted.ResponseMode is ResponseMode.Written or ResponseMode.Combined)
                {
                    validation.Add(NewResult(ctx.Plan, ValidationSeverity.Warning, ValidationCode.AnswerSpaceMissing,
                        $"La pregunta {original.Order + 1} no tiene espacio para responder.", original.Id));
                }
            }

            var roles = extracted.TryGetValue(adapted.QuestionId, out var ex) ? ex.ImageRoles : new List<string>();
            if (roles.Contains("reveals_answer"))
                validation.Add(NewResult(ctx.Plan, ValidationSeverity.Review, ValidationCode.ImageMayRevealAnswer,
                    $"Una imagen de la pregunta {original.Order + 1} está marcada como «revela la respuesta»: revísala antes de exportar.", original.Id));
            else if (roles.Contains("clue"))
                validation.Add(NewResult(ctx.Plan, ValidationSeverity.Warning, ValidationCode.ImageMayRevealAnswer,
                    $"Una imagen de la pregunta {original.Order + 1} actúa como pista; confirma que es intencionado.", original.Id));
        }

        var semaphore = PedagogicalSemaphore.Compute(
            ctx.Plan, effective, validation, id => RuleCatalog.ById.TryGetValue(id, out var r) ? r : ctx.ExtraRules.GetValueOrDefault(id));
        return (validation, semaphore);
    }

    private static ValidationResult NewResult(
        AdaptationPlan plan, ValidationSeverity severity, ValidationCode code, string message, Guid questionId) => new()
    {
        PlanId = plan.Id, Severity = severity, Code = code, Message = message, QuestionId = questionId,
        RequiresReview = severity != ValidationSeverity.Warning
    };

    /// <summary>Replaces the stored validation results with a fresh run and updates the plan status.</summary>
    public async Task<(List<ValidationResult> Validation, SemaphoreReport Semaphore)> RevalidateAndSaveAsync(PlanAssessment ctx, CancellationToken ct)
    {
        var (validation, semaphore) = Validate(ctx);

        _db.ValidationResults.RemoveRange(_db.ValidationResults.Where(v => v.PlanId == ctx.Plan.Id));
        _db.ValidationResults.AddRange(validation);

        if (ctx.Plan.Status is PlanStatus.Generated or PlanStatus.NeedsTeacherReview or PlanStatus.Exported)
            ctx.Plan.Status = validation.Any(v => v.RequiresReview) ? PlanStatus.NeedsTeacherReview : PlanStatus.Generated;

        await _db.SaveChangesAsync(ct);
        return (validation, semaphore);
    }

    public SemaphoreReport ComputeSemaphore(PlanAssessment ctx, IReadOnlyList<ValidationResult> validation)
    {
        var effective = EffectiveAdapted(ctx, out _);
        return PedagogicalSemaphore.Compute(
            ctx.Plan, effective, validation, id => RuleCatalog.ById.TryGetValue(id, out var r) ? r : ctx.ExtraRules.GetValueOrDefault(id));
    }
}

/// <summary>Writes the audit trail (V2 §21).</summary>
public class ChangeLogWriter
{
    private readonly AdaptAulaDbContext _db;

    public ChangeLogWriter(AdaptAulaDbContext db) => _db = db;

    public void Add(
        Guid planId, Guid? questionId, string kind, string ruleId, string before, string after, string reason,
        RiskLevel risk = RiskLevel.Low, TeacherDecision decision = TeacherDecision.Pending, string actor = "")
    {
        _db.ChangeLogRecords.Add(new ChangeLogRecord
        {
            PlanId = planId, QuestionId = questionId, Kind = kind, RuleId = ruleId, Before = before, After = after,
            Reason = reason, Risk = risk, Decision = decision, Actor = actor
        });
    }
}
