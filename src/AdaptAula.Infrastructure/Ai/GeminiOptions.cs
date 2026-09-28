namespace AdaptAula.Infrastructure.Ai;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>Read from configuration/environment (GEMINI__APIKEY or GEMINI_API_KEY) — never
    /// hardcoded. Free-tier key from https://aistudio.google.com/apikey.</summary>
    public string ApiKey { get; set; } = string.Empty;

    // gemini-2.0-flash was decommissioned (June 2026, EOL). gemini-3.5-flash-lite handled document
    // structuring well but was unreliable at also cross-referencing embedded images against the
    // reference-image list in the same call; gemini-3.5-flash did noticeably better at that
    // compound task for a still-negligible per-document cost.
    public string Model { get; set; } = "gemini-3.5-flash";

    /// <summary>Tried after the primary model's own retries are exhausted on a transient failure
    /// (503/429) — a different model has a separate capacity pool, so it's often available even when
    /// the primary is momentarily overloaded. Set empty to disable.</summary>
    public string? FallbackModel { get; set; } = "gemini-3.1-flash-lite";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
}
