using AdaptAula.Api.Dtos;
using AdaptAula.Api.Services;
using AdaptAula.Domain;
using AdaptAula.Infrastructure.Persistence;
using AdaptAula.RulesEngine;
using AdaptAula.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdaptAula.Api.Controllers;

public record PackItemRequest(
    Guid ProfileId, int Level = 2, bool CurricularChangeAuthorized = false, string? CurricularObjective = null,
    string? CurricularReference = null, List<string>? CurricularCriteriaIds = null, List<string>? CurricularContentIds = null);

public record CreatePackRequest(string? Title, List<PackItemRequest> Items);

public record PackPlanState(
    Guid PlanId, Guid ProfileId, string Alias, PlanStatus Status, bool Generated, SemaphoreLevel? Semaphore,
    int Errors, int Reviews, int PendingProposals);

public record PackResponse(Guid Id, Guid AssessmentId, string Title, List<PackPlanState> Plans);

/// <summary>Class pack (V2 §19): upload a test once and get Original + Student A + Student B… Each version is reviewed before export.</summary>
[ApiController]
[Route("api")]
public class PacksController : ControllerBase
{
    private readonly AdaptAulaDbContext _db;
    private readonly PlanDocumentService _documents;
    private readonly PackExportService _packExport;

    public PacksController(AdaptAulaDbContext db, PlanDocumentService documents, PackExportService packExport)
    {
        _db = db;
        _documents = documents;
        _packExport = packExport;
    }

    [HttpPost("assessments/{assessmentId:guid}/packs")]
    public async Task<ActionResult<PackResponse>> Create(Guid assessmentId, CreatePackRequest request, CancellationToken ct)
    {
        var assessment = await _db.Assessments.Include(a => a.Sections).ThenInclude(s => s.Questions)
            .FirstOrDefaultAsync(a => a.Id == assessmentId, ct);
        if (assessment is null) return NotFound("Assessment no encontrado.");
        if (request.Items.Count == 0) return BadRequest(new { error = "Elige al menos un alumno para el pack." });
        if (request.Items.Select(i => i.ProfileId).Distinct().Count() != request.Items.Count)
            return BadRequest(new { error = "Cada alumno solo puede aparecer una vez en el pack." });

        var extra = await _documents.LoadExtraRulesAsync(ct);
        var pack = new ClassPack { AssessmentId = assessmentId, Title = string.IsNullOrWhiteSpace(request.Title) ? assessment.Title : request.Title.Trim() };

        foreach (var item in request.Items)
        {
            var profile = await _db.StudentProfiles.FindAsync(new object[] { item.ProfileId }, ct);
            if (profile is null) return NotFound($"Perfil {item.ProfileId} no encontrado.");

            var plan = PlanResolver.Resolve(new PlanResolverInput
            {
                Assessment = assessment, Profile = profile, Level = item.Level,
                CurricularChangeAuthorized = item.CurricularChangeAuthorized, CurricularObjective = item.CurricularObjective,
                CurricularReference = item.CurricularReference,
                CurricularCriteriaIds = item.CurricularCriteriaIds ?? new List<string>(),
                CurricularContentIds = item.CurricularContentIds ?? new List<string>(),
                ExtraRules = extra.Values.ToList()
            });
            plan.PackId = pack.Id;
            _db.AdaptationPlans.Add(plan);
            pack.PlanIds.Add(plan.Id);
        }

        _db.ClassPacks.Add(pack);
        await _db.SaveChangesAsync(ct);
        return await Describe(pack, ct);
    }

    [HttpGet("packs/{id:guid}")]
    public async Task<ActionResult<PackResponse>> Get(Guid id, CancellationToken ct)
    {
        var pack = await _db.ClassPacks.FindAsync(new object[] { id }, ct);
        return pack is null ? NotFound() : await Describe(pack, ct);
    }

    [HttpGet("assessments/{assessmentId:guid}/packs")]
    public async Task<ActionResult<List<PackSummary>>> List(Guid assessmentId, CancellationToken ct)
    {
        var packs = await _db.ClassPacks.Where(p => p.AssessmentId == assessmentId).OrderByDescending(p => p.CreatedAt).ToListAsync(ct);
        return packs.Select(p => new PackSummary(p.Id, p.Title, p.PlanIds.Count, p.CreatedAt)).ToList();
    }

    private async Task<PackResponse> Describe(ClassPack pack, CancellationToken ct)
    {
        var aliases = await _db.StudentProfiles.ToDictionaryAsync(p => p.Id, p => p.Alias, ct);
        var states = new List<PackPlanState>();

        foreach (var planId in pack.PlanIds)
        {
            var ctx = await _documents.LoadAsync(planId, ct);
            if (ctx is null) continue;

            var generated = ctx.Adapted.Count > 0;
            var validation = generated ? await _db.ValidationResults.Where(v => v.PlanId == planId).ToListAsync(ct) : new List<ValidationResult>();
            SemaphoreLevel? level = generated ? _documents.ComputeSemaphore(ctx, validation).Overall : null;

            states.Add(new PackPlanState(
                planId, ctx.Plan.ProfileId, aliases.GetValueOrDefault(ctx.Plan.ProfileId, "alumno"), ctx.Plan.Status, generated, level,
                validation.Count(v => v.Severity == ValidationSeverity.Error),
                validation.Count(v => v.Severity == ValidationSeverity.Review),
                ctx.Adapted.Count(a => a.Proposal is { Status: ProposalStatus.Pending })));
        }

        return new PackResponse(pack.Id, pack.AssessmentId, pack.Title, states);
    }

    /// <summary>ZIP with the original, every student's version (PDF and/or Word) and the chosen extras (V2 §19, §20).</summary>
    [HttpPost("packs/{id:guid}/export")]
    public async Task<IActionResult> ExportPack(Guid id, ExportBundleRequest request, CancellationToken ct)
    {
        var pack = await _db.ClassPacks.FindAsync(new object[] { id }, ct);
        if (pack is null) return NotFound();

        var contexts = new List<PlanAssessment>();
        foreach (var planId in pack.PlanIds)
            if (await _documents.LoadAsync(planId, ct) is { } ctx) contexts.Add(ctx);

        return await Bundle(contexts, request, $"pack-{ExportService.Slugify(pack.Title)}.zip", ct);
    }

    /// <summary>The same ZIP for one student: their version plus the optional extras (teacher sheet, answer key, history, digital version).</summary>
    [HttpPost("plans/{id:guid}/export-bundle")]
    public async Task<IActionResult> ExportBundle(Guid id, ExportBundleRequest request, CancellationToken ct)
    {
        var ctx = await _documents.LoadAsync(id, ct);
        if (ctx is null) return NotFound();
        return await Bundle(new List<PlanAssessment> { ctx }, request with { IncludeOriginal = false }, $"adaptacion-{ExportService.Slugify(ctx.Assessment.Title)}.zip", ct);
    }

    private async Task<IActionResult> Bundle(List<PlanAssessment> contexts, ExportBundleRequest request, string fileName, CancellationToken ct)
    {
        try
        {
            var bytes = await _packExport.BuildAsync(contexts, new ExportBundleOptions(
                request.Pdf, request.Docx, request.IncludeOriginal, request.TeacherSheet, request.AnswerKey, request.ChangeLog,
                request.AccessibleHtml, request.ApprovedBy ?? string.Empty), ct);
            return File(bytes, "application/zip", fileName);
        }
        catch (BundleBlockedException ex)
        {
            return Conflict(new { error = ex.Message, reasons = ex.Reasons });
        }
    }
}

public record PackSummary(Guid Id, string Title, int Students, DateTime CreatedAt);

public record ExportBundleRequest(
    bool Pdf = true, bool Docx = true, bool IncludeOriginal = true, bool TeacherSheet = true, bool AnswerKey = false,
    bool ChangeLog = false, bool AccessibleHtml = false, string? ApprovedBy = null);
