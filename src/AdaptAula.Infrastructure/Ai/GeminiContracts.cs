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
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    /// <summary>Embeds a PDF, image or other binary blob directly in the request (Gemini's
    /// <c>inline_data</c> part shape) — used to hand the model a document's raw bytes, or a
    /// separately-labeled reference image, alongside plain-text instructions in the same call.</summary>
    [JsonPropertyName("inline_data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public GeminiInlineData? InlineData { get; set; }

    public static GeminiPart FromText(string text) => new() { Text = text };
    public static GeminiPart FromBytes(byte[] bytes, string mimeType) =>
        new() { InlineData = new GeminiInlineData { MimeType = mimeType, Data = Convert.ToBase64String(bytes) } };
}

internal class GeminiInlineData
{
    [JsonPropertyName("mime_type")]
    public string MimeType { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public string Data { get; set; } = string.Empty;
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
    /// <summary>1-based position matching the "PREGUNTA N" numbering the batch prompt gave this
    /// question — resolved back to the request's real Guid inside GeminiAdaptationTextGenerator.
    /// Never exposed outside that class.</summary>
    [JsonPropertyName("question_index")]
    public int QuestionIndex { get; set; }

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

/// <summary>Shape the model is constrained to return via response_schema for the document
/// structuring call — mirrors <see cref="DocumentStructure"/> but as plain JSON-friendly types.</summary>
internal class GeminiDocumentStructurePayload
{
    [JsonPropertyName("confidence")]
    public double Confidence { get; set; }

    [JsonPropertyName("sections")]
    public List<GeminiSectionPayload> Sections { get; set; } = new();
}

internal class GeminiSectionPayload
{
    [JsonPropertyName("stimulus_text")]
    public string? StimulusText { get; set; }

    [JsonPropertyName("start_page")]
    public int StartPage { get; set; }

    [JsonPropertyName("end_page")]
    public int EndPage { get; set; }

    [JsonPropertyName("questions")]
    public List<GeminiQuestionPayload> Questions { get; set; } = new();
}

internal class GeminiQuestionPayload
{
    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("options")]
    public List<string> Options { get; set; } = new();

    [JsonPropertyName("points_hint")]
    public int? PointsHint { get; set; }
}
