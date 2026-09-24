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
    private readonly AdaptAulaDbContext _db;

    public ProfilesController(AdaptAulaDbContext db) => _db = db;

    /// <summary>Necessity presets available to pick from — alias-based profile only, never a
    /// diagnosis field (spec §1 privacy by design). MVP1 surfaces a curated subset.</summary>
    [HttpGet("presets")]
    public ActionResult<List<NecessityPreset>> Presets() =>
        NecessityPresets.All.Where(p => NecessityPresets.Mvp1Keys.Contains(p.Key)).ToList();

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
        var profile = new StudentProfile
        {
            Alias = request.Alias,
            Grade = request.Grade,
            Measures = request.Measures,
            Accommodations = request.Accommodations,
            Exceptions = request.Exceptions
        };

        _db.StudentProfiles.Add(profile);
        await _db.SaveChangesAsync(ct);
        return CreatedAtAction(nameof(Get), new { id = profile.Id }, profile);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var profile = await _db.StudentProfiles.FindAsync(new object[] { id }, ct);
        if (profile is null) return NotFound();
        _db.StudentProfiles.Remove(profile);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
