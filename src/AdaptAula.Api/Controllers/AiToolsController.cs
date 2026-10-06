using AdaptAula.Domain;
using AdaptAula.Infrastructure.Ai;
using AdaptAula.Infrastructure.Persistence;
using AdaptAula.Validation;
using Microsoft.AspNetCore.Mvc;

namespace AdaptAula.Api.Controllers;

public record TextToolApiRequest(TextTool Tool, string Text, Guid? QuestionId = null, string? QuestionContext = null);

public record TextToolApiResponse(string Original, string Proposal, string Note, List<string> Warnings);

/// <summary>AI tools on a selected piece of text (V2 §10). It only ever returns a proposal — "Original → Propuesta" — the
/// teacher decides whether to apply it, and anything that could leak the answer or lower the demand comes back with a warning.</summary>
[ApiController]
[Route("api/ai")]
public class AiToolsController : ControllerBase
{
    private const int MaxTextLength = 4000;

    private readonly ITextToolService _tools;
    private readonly AdaptAulaDbContext _db;

    public AiToolsController(ITextToolService tools, AdaptAulaDbContext db)
    {
        _tools = tools;
        _db = db;
    }

    [HttpPost("text-tool")]
    public async Task<ActionResult<TextToolApiResponse>> RunTextTool(TextToolApiRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Text)) return BadRequest(new { error = "Selecciona un texto primero." });
        if (request.Text.Length > MaxTextLength) return BadRequest(new { error = "La selección es demasiado larga." });

        Question? question = null;
        Assessment? assessment = null;
        if (request.QuestionId is { } questionId)
        {
            question = await _db.Questions.FindAsync(new object[] { questionId }, ct);
            if (question is not null)
            {
                var section = await _db.Sections.FindAsync(new object[] { question.SectionId }, ct);
                if (section is not null) assessment = await _db.Assessments.FindAsync(new object[] { section.AssessmentId }, ct);
            }
        }

        var result = await _tools.RunAsync(new TextToolRequest(
            request.Tool, request.Text, assessment?.Language ?? "es", request.QuestionContext ?? question?.OriginalText,
            assessment?.ProtectedVocabulary ?? new List<string>()), ct);

        var warnings = new List<string>();
        // The expected answer is checked here but was never sent to the AI.
        if (question is not null && TextToolInfo.AddsHelp(request.Tool))
        {
            if (TextHeuristics.ContainsAnswer(result.Proposal, question.ExpectedAnswer))
                warnings.Add("La propuesta contiene la respuesta esperada: no la apliques tal cual.");
            else if (TextHeuristics.AnswerLeakRatio(result.Proposal, question.OriginalText, question.ExpectedAnswer) >= 0.6)
                warnings.Add("La propuesta contiene casi todas las palabras de la respuesta esperada; revisa que no la regale.");
        }

        if (assessment is not null)
        {
            foreach (var term in assessment.ProtectedVocabulary.Where(t => TextHeuristics.ContainsTerm(request.Text, t) && !TextHeuristics.ContainsTerm(result.Proposal, t)))
                warnings.Add($"La propuesta elimina el término protegido «{term}».");
        }

        if (TextToolInfo.MayReduceDemand(request.Tool))
            warnings.Add("Esta herramienta puede facilitar la pregunta: comprueba que sigue evaluando lo mismo.");

        return new TextToolApiResponse(result.Original, result.Proposal, result.Note, warnings);
    }
}
