namespace AdaptAula.Infrastructure.Ai;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>Read from configuration/environment (GEMINI__APIKEY or GEMINI_API_KEY) — never
    /// hardcoded. Free-tier key from https://aistudio.google.com/apikey.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gemini-2.0-flash";

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";
}
