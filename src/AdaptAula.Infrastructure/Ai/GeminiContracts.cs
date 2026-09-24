using System.Text.Json.Serialization;

namespace AdaptAula.Infrastructure.Ai;

// Minimal subset of the Gemini generateContent REST contract — just what this project needs.
// https://ai.google.dev/api/generate-content

internal class GeminiRequest
{
    [JsonPropertyName("system_instruction")]
    public GeminiContent SystemInstruction { get; set; } = new();

    [JsonPropertyName("contents")]
    public List<GeminiContent> Contents { get; set; } = new();

    [JsonPropertyName("generationConfig")]
    public GeminiGenerationConfig GenerationConfig { get; set; } = new();
}

internal class GeminiContent
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("parts")]
    public List<GeminiPart> Parts { get; set; } = new();
}

internal class GeminiPart
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

internal class GeminiGenerationConfig
{
    [JsonPropertyName("response_mime_type")]
    public string ResponseMimeType { get; set; } = "application/json";

    [JsonPropertyName("response_schema")]
    public object? ResponseSchema { get; set; }

    [JsonPropertyName("temperature")]
    public double Temperature { get; set; } = 0.3;
}

internal class GeminiResponse
{
    [JsonPropertyName("candidates")]
    public List<GeminiCandidate> Candidates { get; set; } = new();
}

internal class GeminiCandidate
{
    [JsonPropertyName("content")]
    public GeminiContent? Content { get; set; }

    [JsonPropertyName("finishReason")]
    public string? FinishReason { get; set; }
}

/// <summary>Shape the model is constrained to return via response_schema — mirrors
/// <see cref="AdaptationTextResponse"/> but as plain JSON-friendly types.</summary>
internal class GeminiAdaptedQuestionPayload
{
    [JsonPropertyName("adapted_text")]
    public string AdaptedText { get; set; } = string.Empty;

    [JsonPropertyName("response_mode")]
    public string ResponseMode { get; set; } = "Written";

    [JsonPropertyName("supports")]
    public List<string> Supports { get; set; } = new();

    [JsonPropertyName("change_log")]
    public List<GeminiChangeLogEntryPayload> ChangeLog { get; set; } = new();

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = new();
}

internal class GeminiChangeLogEntryPayload
{
    [JsonPropertyName("rule_id")]
    public string RuleId { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}
