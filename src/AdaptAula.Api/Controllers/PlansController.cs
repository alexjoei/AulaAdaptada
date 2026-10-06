using AdaptAula.Api.Dtos;
using AdaptAula.Api.Services;
using AdaptAula.Domain;
using AdaptAula.Infrastructure.Persistence;
using AdaptAula.RulesEngine;
using AdaptAula.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdaptAula.Api.Controllers;

[ApiController]
[Route("api")]
public class PlansController : ControllerBase
{
    private readonly AdaptAulaDbContext _db;
    private readonly AdaptationPipelineService _pipeline;
    private readonly ExportService _exportService;
    private readonly GenerationProgressTracker _progress;
    private readonly PlanDocumentService _documents;
    private readonly ChangeLogWriter _changeLog;
    private readonly CurriculumService _curriculum;

    public PlansController(
        AdaptAulaDbContext db, AdaptationPipelineService pipeline, ExportService exportService, GenerationProgressTracker progress,
        PlanDocumentService documents, ChangeLogWriter changeLog, CurriculumService curriculum)
    {
        _db = db;
        _pipeline = pipeline;
        _exportService = exportService;
        _progress = progress;
        _documents = documents;
        _changeLog = changeLog;
        _curriculum = curriculum;
    }

    /// <summary>PLAN step (spec §7): deterministic rule resolution only, no text rewritten yet.</summary>
    [HttpPost("assessments/{assessmentId:guid}/plans")]
    public async Task<ActionResult<AdaptationPlan>> CreatePlan(Guid assessmentId, CreatePlanRequest request, CancellationToken ct)
    {
        var assessment = await _db.Assessments.Include(a => a.Sections).ThenInclude(s => s.Questions)
            .FirstOrDefaultAsync(a => a.Id == assessmentId, ct);
        if (assessment is null) return NotFound("Assessment no encontrado.");

        var profile = await _db.StudentProfiles.FindAsync(new object[] { request.ProfileId }, ct);
        if (profile is null) return NotFound("Perfil no encontrado.");

        var extra = await _documents.LoadExtraRulesAsync(ct);

        var plan = PlanResolver.Resolve(new PlanResolverInput
        {
            Assessment = assessment,
            Profile = profile,
            Level = request.Level,
            CurricularChangeAuthorized = request.CurricularChangeAuthorized,
            CurricularObjective = request.CurricularObjective,
            CurricularReference = request.CurricularReference,
            CurricularCriteriaIds = request.CurricularCriteriaIds ?? new List<string>(),
            CurricularContentIds = request.CurricularContentIds ?? new List<string>(),
            ExtraRules = extra.Values.ToList()
        });

        _db.AdaptationPlans.Add(plan);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetPlan), new { id = plan.Id }, plan);
    }

    [HttpGet("assessments/{assessmentId:guid}/plans")]
    public async Task<ActionResult<List<PlanSummary>>> ListPlans(Guid assessmentId, CancellationToken ct)
    {
        var plans = await _db.AdaptationPlans.Where(p => p.AssessmentId == assessmentId).OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
        var aliases = await _db.StudentProfiles.ToDictionaryAsync(p => p.Id, p => p.Alias, ct);
        return plans.Select(p => new PlanSummary(p.Id, p.ProfileId, aliases.GetValueOrDefault(p.ProfileId, "—"), p.Level, p.Status, p.IsCurricularChange, p.CreatedAt, p.PackId)).ToList();
    }

    [HttpGet("plans/{id:guid}")]
    public async Task<ActionResult<AdaptationPlan>> GetPlan(Guid id, CancellationToken ct)
    {
        var plan = await _db.AdaptationPlans.FindAsync(new object[] { id }, ct);
        return plan is null ? NotFound() : plan;
    }

    /// <summary>Measures that apply to this plan, with the rationale shown next to each change in the comparator (V2 §15).</summary>
    [HttpGet("plans/{id:guid}/measures")]
    public async Task<ActionResult<List<AdaptationRule>>> GetPlanMeasures(Guid id, CancellationToken ct)
    {
        var ctx = await _documents.LoadAsync(id, ct);
        if (ctx is null) return NotFound();
        return ctx.Plan.ResolvedRulesByQuestion.Values.SelectMany(l => l).Select(r => r.RuleId).Distinct()
            .Select(rid => RuleCatalog.ById.TryGetValue(rid, out var rule) ? rule : ctx.ExtraRules.GetValueOrDefault(rid))
            .Where(r => r is not null).Select(r => r!).ToList();
    }

    /// <summary>TRANSFORM + VALIDATE (spec §7): batched AI calls, then the deterministic safety validator. Never exposes
    /// an export until this has run and errors are resolved.</summary>
    [HttpPost("plans/{id:guid}/generate")]
    public async Task<ActionResult<PlanStateResponse>> Generate(Guid id, CancellationToken ct)
    {
        var ctx = await _documents.LoadAsync(id, ct);
        if (ctx is null) return NotFound("Plan no encontrado.");
        var plan = ctx.Plan;

        // Remove any previous generation attempt for this plan before re-running it.
        _db.AdaptedQuestions.RemoveRange(ctx.Adapted);
        _db.ValidationResults.RemoveRange(_db.ValidationResults.Where(v => v.PlanId == plan.Id));
        _db.ChangeLogRecords.RemoveRange(_db.ChangeLogRecords.Where(c => c.PlanId == plan.Id));
        plan.Warnings.RemoveAll(w => w.StartsWith('[') || w.StartsWith("UNSUPPORTED_CONFLICT") && false);
        await _db.SaveChangesAsync(ct);

        var referents = _curriculum.DescribeReferents(plan.CurricularCriteriaIds, plan.CurricularContentIds);
        var (adapted, _) = await _pipeline.GenerateAndValidateAsync(
            ctx.Assessment, plan, ctx.Assessment.ExtractionConfidence, ct, ctx.ExtraRules, referents);

        _db.AdaptedQuestions.AddRange(adapted);
        ctx.Adapted.Clear();
        ctx.Adapted.AddRange(adapted);

        plan.DocumentEditedAt = null;
        PlanDocumentService.RegenerateDocumentIfUnedited(ctx);
        plan.Status = PlanStatus.Generated;
        LogGeneration(ctx);
        await _db.SaveChangesAsync(ct);

        await _documents.RevalidateAndSaveAsync(ctx, ct);
        return await BuildState(ctx, ct);
    }

    private void LogGeneration(PlanAssessment ctx)
    {
        foreach (var adapted in ctx.Adapted)
        {
            var original = ctx.Assessment.Sections.SelectMany(s => s.Questions).FirstOrDefault(q => q.Id == adapted.QuestionId);
            foreach (var entry in adapted.ChangeLog)
            {
                var rule = Lookup(ctx, entry.RuleId);
                _changeLog.Add(ctx.Plan.Id, adapted.QuestionId, "generated", entry.RuleId,
                    string.IsNullOrEmpty(entry.Before) ? original?.OriginalText ?? string.Empty : entry.Before,
                    string.IsNullOrEmpty(entry.After) ? adapted.AdaptedText : entry.After,
                    rule?.Rationale is { Length: > 0 } why ? why : entry.Description,
                    rule?.RiskLevel ?? RiskLevel.Low);
            }
            if (adapted.Proposal is not null)
            {
                _changeLog.Add(ctx.Plan.Id, adapted.QuestionId, "proposal", string.Join(",", adapted.Proposal.RuleIds),
                    adapted.AdaptedText, adapted.Proposal.ProposedText, adapted.Proposal.Reason, RiskLevel.High);
            }
        }
    }

    private static AdaptationRule? Lookup(PlanAssessment ctx, string ruleId) =>
        RuleCatalog.ById.TryGetValue(ruleId, out var rule) ? rule : ctx.ExtraRules.GetValueOrDefault(ruleId);

    private async Task<PlanStateResponse> BuildState(PlanAssessment ctx, CancellationToken ct, bool documentOutdated = false)
    {
        var validation = await _db.ValidationResults.Where(v => v.PlanId == ctx.Plan.Id).ToListAsync(ct);
        var adapted = await _db.AdaptedQuestions.Where(a => a.PlanId == ctx.Plan.Id).ToListAsync(ct);
        ctx.Adapted.Clear();
        ctx.Adapted.AddRange(adapted);
        var semaphore = _documents.ComputeSemaphore(ctx, validation);
        return new PlanStateResponse(adapted, validation, SafetyValidator.CanExport(validation), semaphore, documentOutdated);
    }

    [HttpGet("plans/{id:guid}/state")]
    public async Task<ActionResult<PlanStateResponse>> GetState(Guid id, CancellationToken ct)
    {
        var ctx = await _documents.LoadAsync(id, ct);
        return ctx is null ? NotFound() : await BuildState(ctx, ct);
    }

    /// <summary>Polled by the "Generando…" screen while <see cref="Generate"/> is in flight (a
    /// separate request/thread — the tracker is a singleton shared across requests). Returns 204
    /// when nothing is currently generating for this plan, either because it hasn't started yet or
    /// because it already finished.</summary>
    [HttpGet("plans/{id:guid}/generate-progress")]
    public ActionResult<GenerationProgressResponse> GetGenerateProgress(Guid id)
    {
        var progress = _progress.Get(id);
        return progress is null ? NoContent() : new GenerationProgressResponse(progress.Value.Current, progress.Value.Total);
    }

    [HttpGet("plans/{id:guid}/adapted-questions")]
    public async Task<ActionResult<List<AdaptedQuestion>>> GetAdaptedQuestions(Guid id, CancellationToken ct) =>
        await _db.AdaptedQuestions.Where(a => a.PlanId == id).ToListAsync(ct);

    [HttpGet("plans/{id:guid}/validation-results")]
    public async Task<ActionResult<List<ValidationResult>>> GetValidationResults(Guid id, CancellationToken ct) =>
        await _db.ValidationResults.Where(v => v.PlanId == id).ToListAsync(ct);

    [HttpGet("plans/{id:guid}/changelog")]
    public async Task<ActionResult<List<ChangeLogRecord>>> GetChangeLog(Guid id, CancellationToken ct) =>
        await _db.ChangeLogRecords.Where(c => c.PlanId == id).OrderBy(c => c.At).ToListAsync(ct);

    /// <summary>REVIEW step (spec §7): teacher accepts/rejects an individual question. Rejecting reverts that question's
    /// adapted text to the original — it never mutates points/answer.</summary>
    [HttpPost("plans/{id:guid}/review")]
    public async Task<ActionResult<PlanStateResponse>> Review(Guid id, ReviewChangeRequest request, CancellationToken ct)
    {
        var ctx = await _documents.LoadAsync(id, ct);
        if (ctx is null) return NotFound();
        var adaptedQuestion = ctx.Adapted.FirstOrDefault(a => a.Id == request.AdaptedQuestionId);
        if (adaptedQuestion is null) return NotFound();

        adaptedQuestion.TeacherApproved = request.Approved;

        if (!request.Approved)
        {
            var question = PlanDocumentService.QuestionsById(ctx.Assessment).GetValueOrDefault(adaptedQuestion.QuestionId);
            if (question is not null)
            {
                _changeLog.Add(id, question.Id, "review", string.Empty, adaptedQuestion.AdaptedText, question.OriginalText,
                    "El docente rechazó la adaptación de esta pregunta y se restauró el original.", RiskLevel.Low, TeacherDecision.Rejected);
                adaptedQuestion.AdaptedText = question.OriginalText;
                adaptedQuestion.Supports = new List<string>();
                adaptedQuestion.ChangeLog = new List<ChangeLogEntry>();
                adaptedQuestion.Proposal = null;
            }
        }
        else
        {
            _changeLog.Add(id, adaptedQuestion.QuestionId, "review", string.Empty, string.Empty, adaptedQuestion.AdaptedText,
                "El docente aceptó la adaptación de esta pregunta.", RiskLevel.Low, TeacherDecision.Accepted);
        }

        return await SaveTextChange(ctx, ct);
    }

    /// <summary>Teacher edits one question's adapted text: either undoing a single change from the comparator
    /// ("undo", V2 §15) or any other manual edit. Always logged.</summary>
    [HttpPut("plans/{id:guid}/adapted-questions/{adaptedQuestionId:guid}")]
    public async Task<ActionResult<PlanStateResponse>> UpdateAdaptedQuestion(
        Guid id, Guid adaptedQuestionId, UpdateAdaptedQuestionRequest request, CancellationToken ct)
    {
        var ctx = await _documents.LoadAsync(id, ct);
        if (ctx is null) return NotFound();
        var adaptedQuestion = ctx.Adapted.FirstOrDefault(a => a.Id == adaptedQuestionId);
        if (adaptedQuestion is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.AdaptedText)) return BadRequest(new { error = "El texto no puede quedar vacío." });

        var before = adaptedQuestion.AdaptedText;
        adaptedQuestion.AdaptedText = request.AdaptedText;
        if (request.Supports is not null) adaptedQuestion.Supports = request.Supports;

        var undo = request.Kind == "undo";
        var rule = request.RuleId is { Length: > 0 } ? Lookup(ctx, request.RuleId) : null;
        _changeLog.Add(id, adaptedQuestion.QuestionId, undo ? "undo" : "edit", request.RuleId ?? string.Empty, before, request.AdaptedText,
            undo ? $"El docente deshizo un cambio{(rule is null ? "" : $" provocado por: {rule.Description}")}." : "Edición manual del docente.",
            rule?.RiskLevel ?? RiskLevel.Low, undo ? TeacherDecision.Rejected : TeacherDecision.Edited);

        return await SaveTextChange(ctx, ct);
    }

    /// <summary>Accepts or rejects a change the AI only proposed because it might alter what is assessed (V2 §23).</summary>
    [HttpPost("plans/{id:guid}/adapted-questions/{adaptedQuestionId:guid}/proposal")]
    public async Task<ActionResult<PlanStateResponse>> DecideProposal(
        Guid id, Guid adaptedQuestionId, ProposalDecisionRequest request, CancellationToken ct)
    {
        var ctx = await _documents.LoadAsync(id, ct);
        if (ctx is null) return NotFound();
        var adaptedQuestion = ctx.Adapted.FirstOrDefault(a => a.Id == adaptedQuestionId);
        if (adaptedQuestion?.Proposal is null) return NotFound();

        var proposal = adaptedQuestion.Proposal;
        var ruleId = string.Join(",", proposal.RuleIds);
        if (request.Accept)
        {
            _changeLog.Add(id, adaptedQuestion.QuestionId, "proposal", ruleId, adaptedQuestion.AdaptedText, proposal.ProposedText,
                proposal.Reason, RiskLevel.High, TeacherDecision.Accepted);
            adaptedQuestion.AdaptedText = proposal.ProposedText;
            proposal.Status = ProposalStatus.Accepted;
        }
        else
        {
            _changeLog.Add(id, adaptedQuestion.QuestionId, "proposal", ruleId, adaptedQuestion.AdaptedText, proposal.ProposedText,
                proposal.Reason, RiskLevel.High, TeacherDecision.Rejected);
            proposal.Status = ProposalStatus.Rejected;
        }
        // Reassign so EF notices the JSON column changed.
        adaptedQuestion.Proposal = new QuestionProposal
        {
            ProposedText = proposal.ProposedText, RuleIds = proposal.RuleIds, Reason = proposal.Reason, Status = proposal.Status
        };

        return await SaveTextChange(ctx, ct);
    }

    private async Task<ActionResult<PlanStateResponse>> SaveTextChange(PlanAssessment ctx, CancellationToken ct)
    {
        var outdated = ctx.Plan.DocumentEditedAt is not null;
        PlanDocumentService.RegenerateDocumentIfUnedited(ctx);
        await _db.SaveChangesAsync(ct);
        await _documents.RevalidateAndSaveAsync(ctx, ct);
        return await BuildState(ctx, ct, outdated);
    }

    // ---------------------------------------------------------------- the editable document (V2 §9)

    [HttpGet("plans/{id:guid}/document")]
    public async Task<ActionResult<DocumentResponse>> GetDocument(Guid id, CancellationToken ct)
    {
        var ctx = await _documents.LoadAsync(id, ct);
        if (ctx is null) return NotFound();
        if (ctx.Adapted.Count == 0) return Conflict(new { error = "Genera primero los textos adaptados." });

        var html = PlanDocumentService.CurrentHtml(ctx);
        return new DocumentResponse(html, ctx.Plan.Style, ctx.Plan.DocumentEditedAt is not null, ctx.Assessment.Title, ctx.Assessment.Language);
    }

    [HttpPut("plans/{id:guid}/document")]
    [RequestSizeLimit(40_000_000)]
    public async Task<ActionResult<PlanStateResponse>> SaveDocument(Guid id, SaveDocumentRequest request, CancellationToken ct)
    {
        var ctx = await _documents.LoadAsync(id, ct);
        if (ctx is null) return NotFound();
        if (string.IsNullOrWhiteSpace(request.Html)) return BadRequest(new { error = "El documento está vacío." });

        ctx.Plan.DocumentHtml = request.Html;
        ctx.Plan.DocumentEditedAt = DateTime.UtcNow;
        if (request.Style is not null) ctx.Plan.Style = request.Style;
        _changeLog.Add(id, null, "document", string.Empty, string.Empty, string.Empty,
            "El docente guardó cambios en el editor visual.", RiskLevel.Low, TeacherDecision.Edited);

        await _db.SaveChangesAsync(ct);
        await _documents.RevalidateAndSaveAsync(ctx, ct);
        return await BuildState(ctx, ct);
    }

    /// <summary>Records that the teacher applied an AI text tool to part of a question (V2 §10, §21).</summary>
    [HttpPost("plans/{id:guid}/changelog/text-tool")]
    public async Task<IActionResult> LogTextTool(Guid id, TextToolLogRequest request, CancellationToken ct)
    {
        if (await _db.AdaptationPlans.FindAsync(new object[] { id }, ct) is null) return NotFound();
        _changeLog.Add(id, request.QuestionId, "text_tool", "tool:" + request.Tool, request.Before, request.After,
            $"El docente aplicó la herramienta «{request.Tool}» con IA.", RiskLevel.Medium, TeacherDecision.Accepted);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Discards manual edits and rebuilds the document from the adapted questions.</summary>
    [HttpPost("plans/{id:guid}/document/reset")]
    public async Task<ActionResult<PlanStateResponse>> ResetDocument(Guid id, CancellationToken ct)
    {
        var ctx = await _documents.LoadAsync(id, ct);
        if (ctx is null) return NotFound();

        ctx.Plan.DocumentEditedAt = null;
        PlanDocumentService.RegenerateDocumentIfUnedited(ctx);
        _changeLog.Add(id, null, "document", string.Empty, string.Empty, string.Empty,
            "El docente descartó las ediciones y volvió al documento generado.", RiskLevel.Low, TeacherDecision.Rejected);
        await _db.SaveChangesAsync(ct);
        await _documents.RevalidateAndSaveAsync(ctx, ct);
        return await BuildState(ctx, ct);
    }

    /// <summary>EXPORT (spec §7/§12): blocked while any ERROR is unresolved; curricular changes and anything flagged
    /// for review additionally require an explicit approver name.</summary>
    [HttpPost("plans/{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, [FromQuery] ExportFormat format, [FromQuery] string? approvedBy, CancellationToken ct)
    {
        var ctx = await _documents.LoadAsync(id, ct);
        if (ctx is null) return NotFound("Plan no encontrado.");

        try
        {
            var version = await _exportService.ExportAsync(ctx, format, approvedBy ?? string.Empty, ct);
            var bytes = await System.IO.File.ReadAllBytesAsync(version.FilePath, ct);
            var contentType = format == ExportFormat.Docx
                ? "application/vnd.openxmlformats-officedocument.wordprocessingml.document"
                : "application/pdf";
            return File(bytes, contentType, Path.GetFileName(version.FilePath));
        }
        catch (ExportBlockedException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }
}

public record PlanStateResponse(
    List<AdaptedQuestion> AdaptedQuestions, List<ValidationResult> ValidationResults, bool CanExport,
    SemaphoreReport Semaphore, bool DocumentOutdated);

public record DocumentResponse(string Html, DocumentStyle Style, bool Edited, string Title, string Language);

public record PlanSummary(
    Guid Id, Guid ProfileId, string Alias, int Level, PlanStatus Status, bool IsCurricularChange, DateTime CreatedAt, Guid? PackId);

public record GenerationProgressResponse(int Current, int Total);
