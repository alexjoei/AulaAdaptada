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

    public PlansController(AdaptAulaDbContext db, AdaptationPipelineService pipeline, ExportService exportService)
    {
        _db = db;
        _pipeline = pipeline;
        _exportService = exportService;
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

        var plan = PlanResolver.Resolve(new PlanResolverInput
        {
            Assessment = assessment,
            Profile = profile,
            Level = request.Level,
            CurricularChangeAuthorized = request.CurricularChangeAuthorized,
            CurricularObjective = request.CurricularObjective
        });

        _db.AdaptationPlans.Add(plan);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(GetPlan), new { id = plan.Id }, plan);
    }

    [HttpGet("plans/{id:guid}")]
    public async Task<ActionResult<AdaptationPlan>> GetPlan(Guid id, CancellationToken ct)
    {
        var plan = await _db.AdaptationPlans.FindAsync(new object[] { id }, ct);
        return plan is null ? NotFound() : plan;
    }

    /// <summary>TRANSFORM + VALIDATE (spec §7): one AI call per question, then the deterministic
    /// safety validator. Never exposes an export until this has run and errors are resolved.</summary>
    [HttpPost("plans/{id:guid}/generate")]
    public async Task<ActionResult<GenerateResponse>> Generate(Guid id, CancellationToken ct)
    {
        var plan = await _db.AdaptationPlans.FindAsync(new object[] { id }, ct);
        if (plan is null) return NotFound("Plan no encontrado.");

        var assessment = await _db.Assessments.Include(a => a.Sections).ThenInclude(s => s.Questions)
            .FirstOrDefaultAsync(a => a.Id == plan.AssessmentId, ct);
        if (assessment is null) return NotFound("Assessment no encontrado.");

        // Remove any previous generation attempt for this plan before re-running it.
        var previousAdapted = _db.AdaptedQuestions.Where(a => a.PlanId == plan.Id);
        var previousValidation = _db.ValidationResults.Where(v => v.PlanId == plan.Id);
        _db.AdaptedQuestions.RemoveRange(previousAdapted);
        _db.ValidationResults.RemoveRange(previousValidation);
        await _db.SaveChangesAsync(ct);

        var (adapted, validation) = await _pipeline.GenerateAndValidateAsync(assessment, plan, assessment.ExtractionConfidence, ct);

        _db.AdaptedQuestions.AddRange(adapted);
        _db.ValidationResults.AddRange(validation);

        plan.Status = validation.Any(v => v.RequiresReview) ? PlanStatus.NeedsTeacherReview : PlanStatus.Generated;
        await _db.SaveChangesAsync(ct);

        return new GenerateResponse(adapted, validation, SafetyValidator.CanExport(validation));
    }

    [HttpGet("plans/{id:guid}/adapted-questions")]
    public async Task<ActionResult<List<AdaptedQuestion>>> GetAdaptedQuestions(Guid id, CancellationToken ct) =>
        await _db.AdaptedQuestions.Where(a => a.PlanId == id).ToListAsync(ct);

    [HttpGet("plans/{id:guid}/validation-results")]
    public async Task<ActionResult<List<ValidationResult>>> GetValidationResults(Guid id, CancellationToken ct) =>
        await _db.ValidationResults.Where(v => v.PlanId == id).ToListAsync(ct);

    /// <summary>REVIEW step (spec §7): teacher accepts/rejects an individual change. Rejecting
    /// reverts that question's adapted text to the original — it never mutates points/answer.</summary>
    [HttpPost("plans/{id:guid}/review")]
    public async Task<IActionResult> Review(Guid id, ReviewChangeRequest request, CancellationToken ct)
    {
        var adaptedQuestion = await _db.AdaptedQuestions.FindAsync(new object[] { request.AdaptedQuestionId }, ct);
        if (adaptedQuestion is null || adaptedQuestion.PlanId != id) return NotFound();

        adaptedQuestion.TeacherApproved = request.Approved;

        if (!request.Approved)
        {
            var question = await _db.Questions.FindAsync(new object[] { adaptedQuestion.QuestionId }, ct);
            if (question is not null)
            {
                adaptedQuestion.AdaptedText = question.OriginalText;
                adaptedQuestion.Supports = new List<string>();
                adaptedQuestion.ChangeLog = new List<ChangeLogEntry>();
            }
        }

        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>EXPORT (spec §7/§12): blocked while any ERROR is unresolved; curricular changes
    /// additionally require an explicit approver name.</summary>
    [HttpPost("plans/{id:guid}/export")]
    public async Task<IActionResult> Export(Guid id, [FromQuery] ExportFormat format, [FromQuery] string approvedBy, CancellationToken ct)
    {
        var plan = await _db.AdaptationPlans.FindAsync(new object[] { id }, ct);
        if (plan is null) return NotFound("Plan no encontrado.");

        var assessment = await _db.Assessments.Include(a => a.Sections).ThenInclude(s => s.Questions)
            .FirstOrDefaultAsync(a => a.Id == plan.AssessmentId, ct);
        if (assessment is null) return NotFound("Assessment no encontrado.");

        var adaptedQuestions = await _db.AdaptedQuestions.Where(a => a.PlanId == plan.Id).ToListAsync(ct);

        try
        {
            var version = await _exportService.ExportAsync(assessment, plan, adaptedQuestions, format, approvedBy, ct);
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

public record GenerateResponse(List<AdaptedQuestion> AdaptedQuestions, List<ValidationResult> ValidationResults, bool CanExport);
