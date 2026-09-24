using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AdaptAula.Domain;
using Microsoft.Extensions.Options;

namespace AdaptAula.Infrastructure.Ai;

/// <summary>
/// TRANSFORM step (spec §7, §10). Calls Gemini's free tier once per question with the resolved
/// rule instructions and asks it to rewrite presentation/instructions/supports/response mode —
/// never points or the expected answer, which are not part of the response schema at all, so the
/// model has no field through which to change them.
/// </summary>
public class GeminiAdaptationTextGenerator : IAdaptationTextGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly GeminiOptions _options;

    public GeminiAdaptationTextGenerator(HttpClient http, IOptions<GeminiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<AdaptationTextResponse> GenerateAsync(AdaptationTextRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException(
                "Gemini API key not configured. Set it via configuration key 'Gemini:ApiKey' or the GEMINI_API_KEY environment variable.");

        var body = new GeminiRequest
        {
            SystemInstruction = new GeminiContent { Parts = { new GeminiPart { Text = SystemPrompt } } },
            Contents = { new GeminiContent { Role = "user", Parts = { new GeminiPart { Text = BuildUserPrompt(request) } } } },
            GenerationConfig = new GeminiGenerationConfig { ResponseSchema = ResponseSchema }
        };

        var url = $"{_options.BaseUrl}/models/{_options.Model}:generateContent?key={Uri.EscapeDataString(_options.ApiKey)}";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body, JsonOptions), Encoding.UTF8, "application/json")
        };

        using var httpResponse = await _http.SendAsync(httpRequest, ct);
        var responseBody = await httpResponse.Content.ReadAsStringAsync(ct);

        if (!httpResponse.IsSuccessStatusCode)
            throw new InvalidOperationException($"Gemini request failed ({(int)httpResponse.StatusCode}): {responseBody}");

        var parsed = JsonSerializer.Deserialize<GeminiResponse>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("Empty response from Gemini.");

        var text = parsed.Candidates.FirstOrDefault()?.Content?.Parts.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Gemini response had no candidates.");

        var payload = JsonSerializer.Deserialize<GeminiAdaptedQuestionPayload>(text, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse Gemini's structured JSON output.");

        return new AdaptationTextResponse(
            AdaptedText: payload.AdaptedText,
            ResponseMode: Enum.TryParse<ResponseMode>(payload.ResponseMode, true, out var mode) ? mode : ResponseMode.Written,
            Supports: payload.Supports,
            ChangeLog: payload.ChangeLog.Select(c => new ChangeLogEntry
            {
                RuleId = c.RuleId,
                Description = c.Description,
                Category = request.AppliedRules.FirstOrDefault(r => r.RuleId == c.RuleId)?.Category ?? string.Empty,
                Reason = string.Empty
            }).ToList(),
            Warnings: payload.Warnings);
    }

    private static string BuildUserPrompt(AdaptationTextRequest request)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Idioma de la prueba: {request.Language}. Nivel de adaptación: {request.Level}.");
        sb.AppendLine();
        sb.AppendLine("PREGUNTA ORIGINAL:");
        sb.AppendLine(request.Question.OriginalText);
        if (!string.IsNullOrWhiteSpace(request.Question.ExpectedAnswer))
            sb.AppendLine("(La respuesta esperada existe pero NO se te proporciona: no debes inventarla ni revelarla.)");
        if (request.Question.ConstructTags.Count > 0)
            sb.AppendLine($"Constructo evaluado (no debe verse comprometido): {string.Join(", ", request.Question.ConstructTags)}");

        sb.AppendLine();
        sb.AppendLine("REGLAS RESUELTAS A APLICAR (ya decididas, no las cuestiones ni añadas otras):");
        foreach (var rule in request.AppliedRules)
            sb.AppendLine($"- [{rule.RuleId}] ({rule.Category}) {rule.Description}");

        if (request.IsCurricularChange)
        {
            sb.AppendLine();
            sb.AppendLine("CAMBIO CURRICULAR AUTORIZADO POR EL DOCENTE. Objetivo/referente definido por el docente:");
            sb.AppendLine(request.CurricularObjective);
            sb.AppendLine("Adapta la tarea a ese objetivo exacto. No añadas contenido curricular no solicitado.");
        }

        return sb.ToString();
    }

    // Verbatim from spec §10 "Prompt maestro del motor de IA", adapted for a per-question call.
    private const string SystemPrompt = """
        ROL: Eres un motor de adaptación educativa para docentes. No diagnosticas ni decides medidas curriculares.
        OBJETIVO: transformar la presentación, instrucciones, apoyos y/o modo de respuesta de UNA pregunta según las reglas resueltas que se te indiquen, preservando el constructo evaluado y las restricciones.
        REGLAS:
        (1) Respeta todas las reglas resueltas exactamente; no decidas aplicar reglas adicionales ni ignores las indicadas.
        (2) No cambies ni menciones la respuesta correcta ni la puntuación: no forman parte de tu salida.
        (3) Si una adaptación puede revelar la respuesta o reducir la exigencia esencial, no la apliques y explica el motivo en "warnings".
        (4) Mantén el vocabulario curricular imprescindible; acláralo con un apoyo en vez de sustituirlo.
        (5) No añadas información factual que no esté en la fuente, salvo apoyos neutros (glosario, ejemplo de formato) explícitamente indicados en las reglas.
        (6) No uses ningún diagnóstico para inferir nivel intelectual o curricular; solo usa el objetivo curricular si se te indica explícitamente que fue autorizado por el docente.
        (7) Devuelve cambios atómicos y trazables en cambios_log, uno por regla aplicada.
        (8) Si no puedes preservar el constructo evaluado con las reglas dadas, dilo en "warnings" en vez de forzar un cambio.
        SALIDA: JSON estructurado según el esquema proporcionado.
        """;

    private static readonly object ResponseSchema = new
    {
        type = "OBJECT",
        properties = new
        {
            adapted_text = new { type = "STRING" },
            response_mode = new { type = "STRING", @enum = new[] { "Written", "Oral", "Keyboard", "Selection", "Combined" } },
            supports = new { type = "ARRAY", items = new { type = "STRING" } },
            change_log = new
            {
                type = "ARRAY",
                items = new
                {
                    type = "OBJECT",
                    properties = new { rule_id = new { type = "STRING" }, description = new { type = "STRING" } },
                    required = new[] { "rule_id", "description" }
                }
            },
            warnings = new { type = "ARRAY", items = new { type = "STRING" } }
        },
        required = new[] { "adapted_text", "response_mode" }
    };
}
