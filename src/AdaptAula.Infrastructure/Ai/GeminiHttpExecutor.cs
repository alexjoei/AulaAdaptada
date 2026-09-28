using System.Net;

namespace AdaptAula.Infrastructure.Ai;

/// <summary>
/// Shared HTTP-call handling for both Gemini callers (<see cref="GeminiAdaptationTextGenerator"/> and
/// <see cref="GeminiDocumentStructureExtractor"/>): a transient failure (503 UNAVAILABLE — Google's
/// own error explicitly calls demand spikes "usually temporary" — or 429 rate-limited) is retried
/// with backoff, then retried again against a configured fallback model, before finally surfacing as
/// <see cref="AiServiceUnavailableException"/>. A non-transient failure (a real request problem —
/// bad API key, malformed request) is never retried, since retrying it would just waste the backoff
/// time before failing anyway.
/// </summary>
internal static class GeminiHttpExecutor
{
    private static readonly TimeSpan[] DefaultRetryDelays = { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5) };

    /// <param name="retryDelays">Overridable so tests can run this near-instantly (e.g. all-zero
    /// delays) instead of waiting out the real production backoff.</param>
    public static async Task<string> SendWithRetryAsync(
        HttpClient http, GeminiOptions options, Func<string, HttpRequestMessage> requestFactory, CancellationToken ct,
        IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        var delays = retryDelays ?? DefaultRetryDelays;
        var models = new[] { options.Model, options.FallbackModel }
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct()
            .ToList();

        Exception? lastTransientError = null;
        foreach (var model in models)
        {
            for (var attempt = 0; attempt <= delays.Count; attempt++)
            {
                HttpResponseMessage response;
                try
                {
                    using var request = requestFactory(model!);
                    response = await http.SendAsync(request, ct);
                }
                catch (HttpRequestException ex)
                {
                    lastTransientError = ex;
                    if (attempt < delays.Count) { await Task.Delay(delays[attempt], ct); continue; }
                    break; // out of retries for this model — fall through to the next model, if any
                }

                using (response)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    if (response.IsSuccessStatusCode) return body;

                    var isTransient = response.StatusCode is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests;
                    if (!isTransient)
                        throw new InvalidOperationException($"Gemini request failed ({(int)response.StatusCode}): {body}");

                    lastTransientError = new InvalidOperationException($"Gemini request failed ({(int)response.StatusCode}): {body}");
                }

                if (attempt < delays.Count) await Task.Delay(delays[attempt], ct);
            }
        }

        throw new AiServiceUnavailableException(
            "El servicio de IA (Gemini) está saturado o no disponible en este momento. Inténtalo de nuevo en unos minutos.",
            lastTransientError);
    }
}
