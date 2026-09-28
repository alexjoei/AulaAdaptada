using System.Text;
using System.Text.Json;
using AdaptAula.Domain;
using Microsoft.Extensions.Options;

namespace AdaptAula.Infrastructure.Ai;

/// <summary>
/// TRANSFORM step (spec §7, §10). Calls Gemini's free tier once per BATCH of questions (see
/// <see cref="GeminiOptions.AdaptationBatchSize"/>) with each question's own resolved rule
/// instructions, and asks it to rewrite presentation/instructions/supports/response mode per
/// question — never points or the expected answer, which are not part of the response schema at
/// all, so the model has no field through which to change them. Batching (rather than one call per
/// question) is what actually shortens a full-assessment generation: the free tier's bottleneck is
/// requests-per-minute, not per-call latency, so fewer/larger calls help where more concurrent
/// calls would not.
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

    public async Task<IReadOnlyList<AdaptationTextResponse>> GenerateBatchAsync(
        IReadOnlyList<AdaptationTextRequest> requests, CancellationToken ct = default)
    {
        if (requests.Count == 0) return Array.Empty<AdaptationTextResponse>();

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException(
                "Gemini API key not configured. Set it via configuration key 'Gemini:ApiKey' or the GEMINI_API_KEY environment variable.");

        var body = new GeminiRequest
        {
            SystemInstruction = new GeminiContent { Parts = { new GeminiPart { Text = SystemPrompt } } },
            Contents = { new GeminiContent { Role = "user", Parts = { new GeminiPart { Text = BuildBatchUserPrompt(requests) } } } },
            GenerationConfig = new GeminiGenerationConfig { ResponseSchema = ResponseSchema }
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);
        var responseBody = await GeminiHttpExecutor.SendWithRetryAsync(
            _http,
            _options,
            model => new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/models/{model}:generateContent?key={Uri.EscapeDataString(_options.ApiKey)}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            },
            ct);

        var parsed = JsonSerializer.Deserialize<GeminiResponse>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("Empty response from Gemini.");

        var text = parsed.Candidates.FirstOrDefault()?.Content?.Parts.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Gemini response had no candidates.");

        var payloads = JsonSerializer.Deserialize<List<GeminiAdaptedQuestionPayload>>(text, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse Gemini's structured JSON output.");

        var results = new List<AdaptationTextResponse>();
        foreach (var payload in payloads)
        {
            // 1-based "PREGUNTA N" position -> the request it answers. An index outside the batch's
            // range (the model hallucinated one) is skipped rather than crashing the whole batch —
            // the pipeline's per-question fallback covers whatever comes up missing.
            var requestIndex = payload.QuestionIndex - 1;
            if (requestIndex < 0 || requestIndex >= requests.Count) continue;
            var request = requests[requestIndex];

            results.Add(new AdaptationTextResponse(
                QuestionId: request.Question.Id,
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
                Warnings: payload.Warnings));
        }

        return results;
    }

    private static string BuildBatchUserPrompt(IReadOnlyList<AdaptationTextRequest> requests)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Vas a adaptar {requests.Count} preguntas en esta misma llamada. Trátalas de forma completamente independiente: cada una tiene su propio idioma/nivel/reglas y su propio resultado — nunca mezcles apoyos, checklist o contexto de una pregunta con otra.");

        for (var i = 0; i < requests.Count; i++)
        {
            var request = requests[i];
            sb.AppendLine();
            sb.AppendLine($"=== PREGUNTA {i + 1} (question_index = {i + 1}) ===");
            sb.AppendLine($"Idioma de la prueba: {request.Language}. Nivel de adaptación: {request.Level}.");
            sb.AppendLine("PREGUNTA ORIGINAL:");
            sb.AppendLine(request.Question.OriginalText);
            if (!string.IsNullOrWhiteSpace(request.Question.ExpectedAnswer))
                sb.AppendLine("(La respuesta esperada existe pero NO se te proporciona: no debes inventarla ni revelarla.)");
            if (request.Question.ConstructTags.Count > 0)
                sb.AppendLine($"Constructo evaluado (no debe verse comprometido): {string.Join(", ", request.Question.ConstructTags)}");

            sb.AppendLine("REGLAS RESUELTAS A APLICAR A ESTA PREGUNTA (ya decididas, no las cuestiones ni añadas otras):");
            foreach (var rule in request.AppliedRules)
                sb.AppendLine($"- [{rule.RuleId}] ({rule.Category}) {rule.Description}");

            if (request.IsCurricularChange)
            {
                sb.AppendLine("CAMBIO CURRICULAR AUTORIZADO POR EL DOCENTE. Objetivo/referente definido por el docente:");
                sb.AppendLine(request.CurricularObjective);
                sb.AppendLine("Adapta esta pregunta a ese objetivo exacto. No añadas contenido curricular no solicitado.");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"Devuelve un elemento por cada una de las {requests.Count} preguntas, con su \"question_index\" correspondiente (1 a {requests.Count}).");
        return sb.ToString();
    }

    // Verbatim from spec §10 "Prompt maestro del motor de IA", adapted for a batch call.
    private const string SystemPrompt = """
        ROL: Eres un motor de adaptación educativa para docentes. No diagnosticas ni decides medidas curriculares.
        OBJETIVO: transformar la presentación, instrucciones, apoyos y/o modo de respuesta de cada pregunta indicada según las reglas resueltas propias de esa pregunta, preservando su constructo evaluado y sus restricciones.
        REGLAS:
        (1) Respeta todas las reglas resueltas exactamente para cada pregunta; no decidas aplicar reglas adicionales ni ignores las indicadas.
        (2) No cambies ni menciones la respuesta correcta ni la puntuación de ninguna pregunta: no forman parte de tu salida.
        (3) Si una adaptación puede revelar la respuesta o reducir la exigencia esencial de una pregunta, no la apliques a esa pregunta y explica el motivo en sus "warnings".
        (4) Mantén el vocabulario curricular imprescindible; acláralo con un apoyo en vez de sustituirlo.
        (5) No añadas información factual que no esté en la fuente, salvo apoyos neutros (glosario, ejemplo de formato) explícitamente indicados en las reglas de esa pregunta.
        (6) No uses ningún diagnóstico para inferir nivel intelectual o curricular; solo usa el objetivo curricular si se te indica explícitamente que fue autorizado por el docente para esa pregunta.
        (7) Devuelve cambios atómicos y trazables en el "change_log" de cada pregunta, uno por regla aplicada a esa pregunta.
        (8) Si no puedes preservar el constructo evaluado de una pregunta con sus reglas dadas, dilo en sus "warnings" en vez de forzar un cambio.
        (9) Formatea "adapted_text" para que se lea con claridad cuando se imprima: cada paso numerado en su propia línea (usa un salto de línea real entre pasos, nunca los concatenes en una sola frase), y cada opción de respuesta (A, B, C, D...) en su propia línea separada de la pregunta.
        (10) Nunca incluyas casillas de verificación, la palabra "Checklist" ni ningún marcador de progreso (p. ej. "[ ]") dentro de "adapted_text". Cualquier elemento de apoyo, checklist o seguimiento de progreso va exclusivamente en "supports", uno por elemento, sin duplicarlo también en "adapted_text".
        (11) Trata cada pregunta numerada de forma completamente independiente: nunca compartas ni mezcles apoyos, checklist, glosario o contexto entre preguntas distintas, aunque traten un tema parecido.
        (12) Devuelve exactamente un elemento por cada pregunta numerada que se te indique, con su "question_index" igual al número de esa pregunta.
        SALIDA: un array JSON estructurado según el esquema proporcionado, un elemento por pregunta.
        """;

    private static readonly object ResponseSchema = new
    {
        type = "ARRAY",
        items = new
        {
            type = "OBJECT",
            properties = new
            {
                question_index = new { type = "INTEGER" },
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
            required = new[] { "question_index", "adapted_text", "response_mode" }
        }
    };
}
