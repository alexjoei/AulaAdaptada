using System.Text;
using System.Text.Json.Serialization;
using AdaptAula.Domain;

namespace AdaptAula.Infrastructure.Ai;

public record AnalysisCandidate(string Id, string Text);

public record QuestionToAnalyze(Guid QuestionId, string Text, QuestionType Type, int Points, IReadOnlyList<string> Options);

/// <summary>Per-question curricular analysis (V2 §7). The AI only PROPOSES — it may choose criteria/contents solely from the
/// candidate lists it is given, and the teacher confirms or edits every result.</summary>
public interface IQuestionAnalyzer
{
    Task<IReadOnlyList<(Guid QuestionId, QuestionAnalysis Analysis)>> AnalyzeAsync(
        IReadOnlyList<QuestionToAnalyze> questions,
        IReadOnlyList<AnalysisCandidate> criteria,
        IReadOnlyList<AnalysisCandidate> contents,
        string areaName,
        string language,
        CancellationToken ct = default);
}

internal class GeminiAnalysisPayload
{
    [JsonPropertyName("question_index")] public int QuestionIndex { get; set; }
    [JsonPropertyName("content")] public string Content { get; set; } = string.Empty;
    [JsonPropertyName("skill")] public string Skill { get; set; } = string.Empty;
    [JsonPropertyName("cognitive_demand")] public string CognitiveDemand { get; set; } = string.Empty;
    [JsonPropertyName("linguistic_demand")] public string LinguisticDemand { get; set; } = string.Empty;
    [JsonPropertyName("reading_load")] public string ReadingLoad { get; set; } = string.Empty;
    [JsonPropertyName("writing_load")] public string WritingLoad { get; set; } = string.Empty;
    [JsonPropertyName("executive_load")] public string ExecutiveLoad { get; set; } = string.Empty;
    [JsonPropertyName("criteria_ids")] public List<string> CriteriaIds { get; set; } = new();
    [JsonPropertyName("content_ids")] public List<string> ContentIds { get; set; } = new();
}

public class GeminiQuestionAnalyzer : IQuestionAnalyzer
{
    public static readonly string[] CognitiveLevels = { "recordar", "comprender", "aplicar", "analizar", "evaluar", "crear" };
    public static readonly string[] Loads = { "baja", "media", "alta" };

    private readonly GeminiJsonClient _client;

    public GeminiQuestionAnalyzer(GeminiJsonClient client) => _client = client;

    public async Task<IReadOnlyList<(Guid QuestionId, QuestionAnalysis Analysis)>> AnalyzeAsync(
        IReadOnlyList<QuestionToAnalyze> questions, IReadOnlyList<AnalysisCandidate> criteria, IReadOnlyList<AnalysisCandidate> contents,
        string areaName, string language, CancellationToken ct = default)
    {
        if (questions.Count == 0) return Array.Empty<(Guid, QuestionAnalysis)>();

        var prompt = new StringBuilder();
        prompt.AppendLine($"Área: {areaName}. Idioma de la prueba: {language}.");
        prompt.AppendLine("CRITERIOS DE EVALUACIÓN CANDIDATOS (usa SOLO estos ids):");
        foreach (var c in criteria) prompt.AppendLine($"- {c.Id}: {c.Text}");
        prompt.AppendLine("SABERES BÁSICOS CANDIDATOS (usa SOLO estos ids):");
        foreach (var c in contents) prompt.AppendLine($"- {c.Id}: {c.Text}");

        for (var i = 0; i < questions.Count; i++)
        {
            var q = questions[i];
            prompt.AppendLine();
            prompt.AppendLine($"=== PREGUNTA {i + 1} (question_index = {i + 1}) · tipo {q.Type} · {q.Points} puntos ===");
            prompt.AppendLine(q.Text);
            foreach (var option in q.Options) prompt.AppendLine($"  - {option}");
        }

        var payloads = await _client.GenerateAsync<List<GeminiAnalysisPayload>>(SystemPrompt, prompt.ToString(), Schema, ct, temperature: 0.1);

        var validCriteria = criteria.Select(c => c.Id).ToHashSet();
        var validContents = contents.Select(c => c.Id).ToHashSet();
        var results = new List<(Guid, QuestionAnalysis)>();
        foreach (var p in payloads)
        {
            var index = p.QuestionIndex - 1;
            if (index < 0 || index >= questions.Count) continue;
            results.Add((questions[index].QuestionId, new QuestionAnalysis
            {
                Content = p.Content,
                Skill = p.Skill,
                CognitiveDemand = Normalize(p.CognitiveDemand, CognitiveLevels),
                LinguisticDemand = Normalize(p.LinguisticDemand, Loads),
                ReadingLoad = Normalize(p.ReadingLoad, Loads),
                WritingLoad = Normalize(p.WritingLoad, Loads),
                ExecutiveLoad = Normalize(p.ExecutiveLoad, Loads),
                // Never trust an id the model invented: only candidates survive.
                CriteriaIds = p.CriteriaIds.Where(validCriteria.Contains).Distinct().ToList(),
                ContentIds = p.ContentIds.Where(validContents.Contains).Distinct().ToList(),
                Source = "ai",
                Confirmed = false
            }));
        }
        return results;
    }

    private static string Normalize(string value, string[] allowed)
    {
        var v = (value ?? string.Empty).Trim().ToLowerInvariant();
        return allowed.Contains(v) ? v : string.Empty;
    }

    private const string SystemPrompt = """
        ROL: Eres un asistente de análisis curricular para docentes de Primaria (LOMLOE). Analizas cada pregunta de una prueba.
        Para cada pregunta devuelve: "content" (qué contenido evalúa, en una frase), "skill" (qué habilidad pone en juego), "cognitive_demand" (recordar, comprender, aplicar, analizar, evaluar o crear), "linguistic_demand", "reading_load", "writing_load" y "executive_load" (cada una: baja, media o alta), y los ids de criterios de evaluación y saberes básicos de las listas candidatas que la pregunta evalúa.
        REGLAS: usa únicamente ids de las listas dadas; si ninguno encaja deja la lista vacía (no inventes). No diagnostiques ni deduzcas nada sobre el alumnado. Tu salida es una propuesta que el docente confirmará. Un elemento por pregunta con su question_index.
        """;

    private static readonly object Schema = new
    {
        type = "ARRAY",
        items = new
        {
            type = "OBJECT",
            properties = new
            {
                question_index = new { type = "INTEGER" },
                content = new { type = "STRING" },
                skill = new { type = "STRING" },
                cognitive_demand = new { type = "STRING", @enum = CognitiveLevels },
                linguistic_demand = new { type = "STRING", @enum = Loads },
                reading_load = new { type = "STRING", @enum = Loads },
                writing_load = new { type = "STRING", @enum = Loads },
                executive_load = new { type = "STRING", @enum = Loads },
                criteria_ids = new { type = "ARRAY", items = new { type = "STRING" } },
                content_ids = new { type = "ARRAY", items = new { type = "STRING" } }
            },
            required = new[] { "question_index", "content", "skill", "cognitive_demand", "criteria_ids", "content_ids" }
        }
    };
}
