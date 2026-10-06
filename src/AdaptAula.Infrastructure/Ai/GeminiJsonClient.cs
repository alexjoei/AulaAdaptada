using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AdaptAula.Infrastructure.Ai;

/// <summary>Thin shared helper for "send a prompt, get schema-constrained JSON back" — used by the text tools and the
/// curricular analyzer. Reuses the same retry/fallback-model handling as the other Gemini callers.</summary>
public class GeminiJsonClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly GeminiOptions _options;

    public GeminiJsonClient(HttpClient http, IOptions<GeminiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<T> GenerateAsync<T>(string systemPrompt, string userPrompt, object responseSchema, CancellationToken ct, double temperature = 0.3)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException(
                "Gemini API key not configured. Set it via configuration key 'Gemini:ApiKey' or the GEMINI_API_KEY environment variable.");

        var body = new GeminiRequest
        {
            SystemInstruction = new GeminiContent { Parts = { new GeminiPart { Text = systemPrompt } } },
            Contents = { new GeminiContent { Role = "user", Parts = { new GeminiPart { Text = userPrompt } } } },
            GenerationConfig = new GeminiGenerationConfig { ResponseSchema = responseSchema, Temperature = temperature }
        };

        var json = JsonSerializer.Serialize(body, JsonOptions);
        var responseBody = await GeminiHttpExecutor.SendWithRetryAsync(
            _http, _options,
            model => new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/models/{model}:generateContent?key={Uri.EscapeDataString(_options.ApiKey)}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            },
            ct);

        var parsed = JsonSerializer.Deserialize<GeminiResponse>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("Empty response from Gemini.");
        var text = parsed.Candidates.FirstOrDefault()?.Content?.Parts.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Gemini response had no candidates.");

        return JsonSerializer.Deserialize<T>(text, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse Gemini's structured JSON output.");
    }
}
