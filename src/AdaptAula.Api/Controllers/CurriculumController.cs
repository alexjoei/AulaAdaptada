using AdaptAula.Api.Services;
using AdaptAula.RulesEngine;
using Microsoft.AspNetCore.Mvc;

namespace AdaptAula.Api.Controllers;

/// <summary>The LOMLOE curriculum as its own layer (V2 §6, §13): nothing here depends on a need or diagnosis.</summary>
[ApiController]
[Route("api/curriculum")]
public class CurriculumController : ControllerBase
{
    private readonly CurriculumService _curriculum;

    public CurriculumController(CurriculumService curriculum) => _curriculum = curriculum;

    [HttpGet]
    public ActionResult<List<CurriculumSummary>> List() =>
        _curriculum.All.Select(c => new CurriculumSummary(
            c.Id, c.Ccaa, c.Stage, c.LegalBasis, c.Verified, c.Note, c.Cycles,
            c.Areas.Select(a => new AreaSummary(a.Id, a.Name)).ToList())).ToList();

    [HttpGet("{id}")]
    public ActionResult<Curriculum> Get(string id) =>
        _curriculum.Get(id) is { } curriculum ? curriculum : NotFound();
}

public record AreaSummary(string Id, string Name);

public record CurriculumSummary(
    string Id, string Ccaa, string Stage, string LegalBasis, bool Verified, string Note,
    List<CurriculumCycle> Cycles, List<AreaSummary> Areas);
