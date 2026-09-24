using AdaptAula.Api.Dtos;
using AdaptAula.Domain;
using AdaptAula.Infrastructure.Ingestion;
using AdaptAula.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AdaptAula.Api.Controllers;

[ApiController]
[Route("api/assessments")]
public class AssessmentsController : ControllerBase
{
    private readonly AdaptAulaDbContext _db;
    private readonly DocumentIngestionService _ingestion;

    public AssessmentsController(AdaptAulaDbContext db, DocumentIngestionService ingestion)
    {
        _db = db;
        _ingestion = ingestion;
    }

    [HttpGet]
    public async Task<ActionResult<List<Assessment>>> List(CancellationToken ct) =>
        await _db.Assessments.Include(a => a.Sections).ThenInclude(s => s.Questions).OrderByDescending(a => a.CreatedAt).ToListAsync(ct);

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Assessment>> Get(Guid id, CancellationToken ct)
    {
        var assessment = await _db.Assessments.Include(a => a.Sections).ThenInclude(s => s.Questions)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        return assessment is null ? NotFound() : assessment;
    }

    [HttpPost("text")]
    public async Task<ActionResult<Assessment>> CreateFromText(CreateAssessmentFromTextRequest request, CancellationToken ct)
    {
        var result = _ingestion.IngestPlainText(request.Title, request.Text);
        return await Persist(result, request.Grade, request.Subject, request.Language, ct);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<Assessment>> Upload(
        IFormFile file, [FromForm] int grade, [FromForm] string subject, [FromForm] string language, CancellationToken ct)
    {
        if (file.Length == 0) return BadRequest("Archivo vacío.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        await using var stream = file.OpenReadStream();

        var result = extension switch
        {
            ".docx" => _ingestion.IngestDocx(file.FileName, stream),
            ".pdf" => _ingestion.IngestPdf(file.FileName, stream),
            _ => throw new InvalidOperationException($"Formato no soportado en MVP1: {extension}. Usa DOCX, PDF o pega el texto.")
        };

        return await Persist(result, grade, subject, language, ct);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<Assessment>> Update(Guid id, UpdateAssessmentRequest request, CancellationToken ct)
    {
        var assessment = await _db.Assessments.Include(a => a.Sections).ThenInclude(s => s.Questions)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (assessment is null) return NotFound();

        if (request.Title is not null) assessment.Title = request.Title;
        if (request.Grade is not null) assessment.Grade = request.Grade.Value;
        if (request.Subject is not null) assessment.Subject = request.Subject;
        if (request.LockedFields is not null) assessment.LockedFields = request.LockedFields;

        if (request.Questions is not null)
        {
            var questionsById = assessment.Sections.SelectMany(s => s.Questions).ToDictionary(q => q.Id);
            foreach (var edit in request.Questions)
            {
                if (!questionsById.TryGetValue(edit.Id, out var question)) continue;
                if (edit.Points is not null) question.Points = edit.Points.Value;
                if (edit.ExpectedAnswer is not null) question.ExpectedAnswer = edit.ExpectedAnswer;
                if (edit.ConstructTags is not null) question.ConstructTags = edit.ConstructTags;
                if (edit.Type is not null) question.Type = edit.Type.Value;
            }
            assessment.TotalPoints = assessment.Sections.SelectMany(s => s.Questions).Sum(q => q.Points);
        }

        await _db.SaveChangesAsync(ct);
        return assessment;
    }

    private async Task<ActionResult<Assessment>> Persist(
        IngestionResult result, int grade, string subject, string language, CancellationToken ct)
    {
        result.Assessment.Grade = grade;
        result.Assessment.Subject = subject;
        result.Assessment.Language = language;
        result.Assessment.ExtractionConfidence = result.ExtractionConfidence;
        // Sensible defaults a teacher can immediately relax or tighten in the Analysis screen.
        result.Assessment.LockedFields = new List<string> { "content", "criteria", "total_points", "language", "grade" };

        _db.Assessments.Add(result.Assessment);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = result.Assessment.Id }, result.Assessment);
    }
}
