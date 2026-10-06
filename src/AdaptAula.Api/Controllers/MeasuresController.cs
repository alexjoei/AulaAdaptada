using AdaptAula.Api.Dtos;
using AdaptAula.Domain;
using AdaptAula.Infrastructure.Persistence;
using AdaptAula.RulesEngine;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdaptAula.Api.Controllers;

/// <summary>The measure library as data (V2 §1–3, §22): groups, every atomic measure, the needs that bundle them, and
/// the teacher's own custom measures.</summary>
[ApiController]
[Route("api/measures")]
public class MeasuresController : ControllerBase
{
    private readonly AdaptAulaDbContext _db;

    public MeasuresController(AdaptAulaDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<MeasureLibraryResponse>> Get(CancellationToken ct)
    {
        var library = MeasureLibrary.Default;
        var custom = (await _db.CustomMeasures.OrderBy(c => c.CreatedAt).ToListAsync(ct)).Select(CustomMeasureFactory.ToRule).ToList();
        return new MeasureLibraryResponse(
            library.Groups, NecessityPresets.Visible.ToList(), library.Rules.Concat(custom).ToList());
    }

    [HttpPost("custom")]
    public async Task<ActionResult<AdaptationRule>> CreateCustom(CreateCustomMeasureRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Description))
            return BadRequest(new { error = "Describe la medida." });
        if (request.Description.Length > 400)
            return BadRequest(new { error = "La descripción no puede superar 400 caracteres." });

        var custom = new CustomMeasure
        {
            Id = CustomMeasureFactory.NewId(),
            Description = request.Description.Trim(),
            Group = request.Group,
            Kind = request.Kind == MeasureKind.Style ? MeasureKind.Text : request.Kind, // custom measures can't carry a look
            AltersAssessedConstruct = request.AltersAssessedConstruct
        };
        _db.CustomMeasures.Add(custom);
        await _db.SaveChangesAsync(ct);
        return CustomMeasureFactory.ToRule(custom);
    }

    [HttpDelete("custom/{id}")]
    public async Task<IActionResult> DeleteCustom(string id, CancellationToken ct)
    {
        var custom = await _db.CustomMeasures.FindAsync(new object[] { id }, ct);
        if (custom is null) return NotFound();
        _db.CustomMeasures.Remove(custom);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}

public record MeasureLibraryResponse(
    IReadOnlyList<MeasureGroupInfo> Groups, List<NecessityPreset> Needs, List<AdaptationRule> Measures);
