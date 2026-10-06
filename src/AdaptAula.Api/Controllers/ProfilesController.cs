using AdaptAula.Api.Dtos;
using AdaptAula.Domain;
using AdaptAula.Infrastructure.Persistence;
using AdaptAula.RulesEngine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdaptAula.Api.Controllers;

[ApiController]
[Route("api/profiles")]
public class ProfilesController : ControllerBase
{
    private const int MaxAliasLength = 40;

    private readonly AdaptAulaDbContext _db;

    public ProfilesController(AdaptAulaDbContext db) => _db = db;

    /// <summary>Needs available to pick from — alias-based profile only, never a diagnosis field (spec §1 privacy by
    /// design). Each need lists its measures with a classification.</summary>
    [HttpGet("presets")]
    public ActionResult<List<NecessityPreset>> Presets() => NecessityPresets.Visible.ToList();

    /// <summary>What a given combination of needs + individual choices really switches on: atomic measures, duplicates
    /// removed, conflicts flagged (V2 §18). The profile editor shows exactly this.</summary>
    [HttpPost("effective-measures")]
    public async Task<ActionResult<List<EffectiveMeasure>>> EffectiveMeasures(EffectiveMeasuresRequest request, CancellationToken ct)
    {
        var extra = (await _db.CustomMeasures.ToListAsync(ct)).Select(CustomMeasureFactory.ToRule).ToDictionary(r => r.Id);
        return MeasureSelection.Compute(request.Needs, request.Accommodations, request.Exceptions, request.SchemaVersion, extra);
    }

    [HttpGet]
    public async Task<ActionResult<List<StudentProfile>>> List(CancellationToken ct) =>
        await _db.StudentProfiles.OrderBy(p => p.Alias).ToListAsync(ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<StudentProfile>> Get(Guid id, CancellationToken ct)
    {
        var profile = await _db.StudentProfiles.FindAsync(new object[] { id }, ct);
        return profile is null ? NotFound() : profile;
    }

    [HttpPost]
    public async Task<ActionResult<StudentProfile>> Create(CreateProfileRequest request, CancellationToken ct)
    {
        var invalid = Validate(request);
        if (invalid is not null) return invalid;

        var profile = new StudentProfile();
        Apply(profile, request);

        _db.StudentProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = profile.Id }, profile);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<StudentProfile>> Update(Guid id, CreateProfileRequest request, CancellationToken ct)
    {
        var profile = await _db.StudentProfiles.FindAsync(new object[] { id }, ct);
        if (profile is null) return NotFound();

        var invalid = Validate(request);
        if (invalid is not null) return invalid;

        Apply(profile, request);
        profile.SchemaVersion = 2; // saving from the V2 editor means the teacher confirmed the explicit selection
        profile.ReviewDate = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return profile;
    }

    /// <summary>Copies a profile under a new alias — two students with the same need can start from the same
    /// measures and then diverge (V2 §4).</summary>
    [HttpPost("{id:guid}/duplicate")]
    public async Task<ActionResult<StudentProfile>> Duplicate(Guid id, [FromQuery] string alias, CancellationToken ct)
    {
        var source = await _db.StudentProfiles.FindAsync(new object[] { id }, ct);
        if (source is null) return NotFound();
        if (string.IsNullOrWhiteSpace(alias) || alias.Trim().Length > MaxAliasLength)
            return BadRequest(new { error = $"Indica un alias de hasta {MaxAliasLength} caracteres." });

        var copy = new StudentProfile
        {
            Alias = alias.Trim(),
            Grade = source.Grade,
            Measures = new List<string>(source.Measures),
            Accommodations = new List<string>(source.Accommodations),
            Exceptions = new List<string>(source.Exceptions),
            Settings = new Dictionary<string, string>(source.Settings),
            SchemaVersion = source.SchemaVersion
        };
        _db.StudentProfiles.Add(copy);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = copy.Id }, copy);
    }

    /// <summary>Deleting a student also deletes every adaptation made for them (V2 §24: sensitive educational data
    /// must be removable).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var profile = await _db.StudentProfiles.FindAsync(new object[] { id }, ct);
        if (profile is null) return NotFound();

        var planIds = await _db.AdaptationPlans.Where(p => p.ProfileId == id).Select(p => p.Id).ToListAsync(ct);
        if (planIds.Count > 0)
        {
            _db.AdaptedQuestions.RemoveRange(_db.AdaptedQuestions.Where(a => planIds.Contains(a.PlanId)));
            _db.ValidationResults.RemoveRange(_db.ValidationResults.Where(v => planIds.Contains(v.PlanId)));
            _db.ChangeLogRecords.RemoveRange(_db.ChangeLogRecords.Where(c => planIds.Contains(c.PlanId)));
            _db.ExportVersions.RemoveRange(_db.ExportVersions.Where(v => planIds.Contains(v.PlanId)));
            _db.AdaptationPlans.RemoveRange(_db.AdaptationPlans.Where(p => planIds.Contains(p.Id)));
        }

        _db.StudentProfiles.Remove(profile);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private ActionResult? Validate(CreateProfileRequest request)
    {
        var alias = request.Alias?.Trim() ?? string.Empty;
        if (alias.Length == 0) return BadRequest(new { error = "El alias es obligatorio." });
        if (alias.Length > MaxAliasLength) return BadRequest(new { error = $"El alias no puede superar {MaxAliasLength} caracteres." });
        return null;
    }

    private static void Apply(StudentProfile profile, CreateProfileRequest request)
    {
        profile.Alias = request.Alias.Trim();
        profile.Grade = request.Grade;
        profile.Measures = request.Measures.Distinct().ToList();
        profile.Accommodations = request.Accommodations.Distinct().ToList();
        profile.Exceptions = request.Exceptions.Distinct().ToList();
        profile.Settings = request.Settings is null ? new() : new Dictionary<string, string>(request.Settings);
    }
}
