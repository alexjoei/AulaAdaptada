using AdaptAula.Api.Dtos;
using AdaptAula.Api.Services;
using AdaptAula.Domain;
using AdaptAula.Infrastructure.Ai;
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
    private readonly IQuestionAnalyzer _analyzer;
    private readonly CurriculumService _curriculum;
    private readonly IWebHostEnvironment _env;

    public AssessmentsController(
        AdaptAulaDbContext db, DocumentIngestionService ingestion, IQuestionAnalyzer analyzer, CurriculumService curriculum, IWebHostEnvironment env)
    {
        _db = db;
        _ingestion = ingestion;
        _analyzer = analyzer;
        _curriculum = curriculum;
        _env = env;
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
        var result = await _ingestion.IngestPlainTextAsync(request.Title, request.Text, ct);
        return await Persist(result, request.Grade, request.Subject, request.Language, ct);
    }

    [HttpPost("upload")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<Assessment>> Upload(
        IFormFile file, [FromForm] int? grade, [FromForm] string? subject, [FromForm] string language, CancellationToken ct)
    {
        if (file.Length == 0) return BadRequest("Archivo vacío.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        await using var stream = file.OpenReadStream();

        var result = extension switch
        {
            ".docx" => await _ingestion.IngestDocxAsync(file.FileName, stream, ct),
            ".pdf" => await _ingestion.IngestPdfAsync(file.FileName, stream, ct),
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
        if (request.LockedFields is not null) assessment.LockedFields = request.LockedFields.Where(ProtectedElements.All.Contains).Distinct().ToList();
        if (request.ProtectedVocabulary is not null)
            assessment.ProtectedVocabulary = request.ProtectedVocabulary.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (request.CurriculumId is not null) assessment.CurriculumId = request.CurriculumId.Length == 0 ? null : request.CurriculumId;
        if (request.CurriculumAreaId is not null) assessment.CurriculumAreaId = request.CurriculumAreaId.Length == 0 ? null : request.CurriculumAreaId;

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
                if (edit.Analysis is not null)
                {
                    var criteriaValid = _curriculum.CriteriaTexts();
                    question.Analysis = new QuestionAnalysis
                    {
                        Content = edit.Analysis.Content, Skill = edit.Analysis.Skill, CognitiveDemand = edit.Analysis.CognitiveDemand,
                        LinguisticDemand = edit.Analysis.LinguisticDemand, ReadingLoad = edit.Analysis.ReadingLoad,
                        WritingLoad = edit.Analysis.WritingLoad, ExecutiveLoad = edit.Analysis.ExecutiveLoad,
                        CriteriaIds = edit.Analysis.CriteriaIds.Where(criteriaValid.ContainsKey).Distinct().ToList(),
                        ContentIds = edit.Analysis.ContentIds.Distinct().ToList(),
                        Source = edit.Analysis.Source, Confirmed = edit.Analysis.Confirmed
                    };
                }
            }
            assessment.TotalPoints = assessment.Sections.SelectMany(s => s.Questions).Sum(q => q.Points);
        }

        await _db.SaveChangesAsync(ct);
        return assessment;
    }

    /// <summary>The AI proposes, per question, the content, skill, cognitive/linguistic/reading/writing/executive demand and the
    /// related LOMLOE criteria and contents (V2 §7). It can only pick from the curriculum's own ids; the teacher confirms.</summary>
    [HttpPost("{id:guid}/analyze")]
    public async Task<ActionResult<Assessment>> Analyze(Guid id, CancellationToken ct)
    {
        var assessment = await _db.Assessments.Include(a => a.Sections).ThenInclude(s => s.Questions)
            .FirstOrDefaultAsync(a => a.Id == id, ct);
        if (assessment is null) return NotFound();

        var candidates = _curriculum.CandidatesFor(assessment);
        if (candidates is null)
            return BadRequest(new { error = "Elige primero el currículo y el área de la prueba para poder proponer criterios y saberes." });

        var toAnalyze = assessment.Sections.SelectMany(s => s.Questions.OrderBy(q => q.Order))
            .Where(q => q.Analysis is not { Confirmed: true })
            .Select(q => new QuestionToAnalyze(q.Id, q.OriginalText, q.Type, q.Points, q.Options))
            .ToList();

        var results = await _analyzer.AnalyzeAsync(toAnalyze, candidates.Criteria, candidates.Contents, candidates.AreaName, assessment.Language, ct);
        var byId = assessment.Sections.SelectMany(s => s.Questions).ToDictionary(q => q.Id);
        foreach (var (questionId, analysis) in results)
            if (byId.TryGetValue(questionId, out var question)) question.Analysis = analysis;

        await _db.SaveChangesAsync(ct);
        return assessment;
    }

    /// <summary>Removes the test and everything generated from it — adaptations, validations, history, exported files (V2 §24).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var assessment = await _db.Assessments.FindAsync(new object[] { id }, ct);
        if (assessment is null) return NotFound();

        var planIds = await _db.AdaptationPlans.Where(p => p.AssessmentId == id).Select(p => p.Id).ToListAsync(ct);
        var versions = await _db.ExportVersions.Where(v => v.AssessmentId == id).ToListAsync(ct);
        foreach (var version in versions)
        {
            try
            {
                var directory = Path.GetDirectoryName(version.FilePath);
                if (directory is not null && Directory.Exists(directory) &&
                    Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.Combine(_env.ContentRootPath, "App_Data", "exports")), StringComparison.OrdinalIgnoreCase))
                    Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // A locked/missing export folder must not stop the data itself from being deleted.
            }
        }

        _db.ExportVersions.RemoveRange(versions);
        _db.AdaptedQuestions.RemoveRange(_db.AdaptedQuestions.Where(a => planIds.Contains(a.PlanId)));
        _db.ValidationResults.RemoveRange(_db.ValidationResults.Where(v => planIds.Contains(v.PlanId)));
        _db.ChangeLogRecords.RemoveRange(_db.ChangeLogRecords.Where(c => planIds.Contains(c.PlanId)));
        _db.AdaptationPlans.RemoveRange(_db.AdaptationPlans.Where(p => p.AssessmentId == id));
        _db.ClassPacks.RemoveRange(_db.ClassPacks.Where(p => p.AssessmentId == id));
        _db.Assessments.Remove(assessment);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<ActionResult<Assessment>> Persist(
        IngestionResult result, int? grade, string? subject, string language, CancellationToken ct)
    {
        result.Assessment.Grade = grade;
        result.Assessment.Subject = subject;
        result.Assessment.Language = language;
        result.Assessment.ExtractionConfidence = result.ExtractionConfidence;
        // Sensible defaults a teacher can immediately relax or tighten in the Analysis screen.
        result.Assessment.LockedFields = ProtectedElements.Defaults.ToList();

        _db.Assessments.Add(result.Assessment);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = result.Assessment.Id }, result.Assessment);
    }
}
