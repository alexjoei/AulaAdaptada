using System.Net;
using AdaptAula.Infrastructure.Ai;
using Xunit;

namespace AdaptAula.Tests;

/// <summary>Replays a scripted sequence of responses, one per call, and records every URL it was
/// asked to hit — enough to verify both what GeminiHttpExecutor did (retried? fell back to a
/// different model?) and how many times it actually called out.</summary>
internal class ScriptedHandler : HttpMessageHandler
{
    private readonly Queue<HttpResponseMessage> _responses;
    public List<string> RequestedUrls { get; } = new();

    public ScriptedHandler(params HttpResponseMessage[] responses) => _responses = new Queue<HttpResponseMessage>(responses);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        RequestedUrls.Add(request.RequestUri!.ToString());
        if (_responses.Count == 0) throw new InvalidOperationException("ScriptedHandler ran out of scripted responses.");
        return Task.FromResult(_responses.Dequeue());
    }
}

public class GeminiHttpExecutorTests
{
    private static readonly IReadOnlyList<TimeSpan> NoDelay = new[] { TimeSpan.Zero, TimeSpan.Zero };

    private static GeminiOptions Options(string? fallback = "fallback-model") => new()
    {
        ApiKey = "test-key",
        Model = "primary-model",
        FallbackModel = fallback,
        BaseUrl = "https://example.test"
    };

    private static HttpResponseMessage Response(HttpStatusCode status, string body = "") =>
        new(status) { Content = new StringContent(body) };

    [Fact]
    public async Task SendWithRetryAsync_SucceedsAfterATransientFailure_WithoutFallingBackToAnotherModel()
    {
        var handler = new ScriptedHandler(
            Response(HttpStatusCode.ServiceUnavailable, "{\"error\":\"busy\"}"),
            Response(HttpStatusCode.OK, "ok-body"));
        using var http = new HttpClient(handler);

        var result = await GeminiHttpExecutor.SendWithRetryAsync(
            http, Options(), model => new HttpRequestMessage(HttpMethod.Get, $"https://example.test/{model}"), CancellationToken.None, NoDelay);

        Assert.Equal("ok-body", result);
        Assert.Equal(2, handler.RequestedUrls.Count);
        Assert.All(handler.RequestedUrls, url => Assert.Contains("primary-model", url));
    }

    [Fact]
    public async Task SendWithRetryAsync_FallsBackToTheSecondModel_WhenThePrimaryExhaustsItsRetries()
    {
        var handler = new ScriptedHandler(
            Response(HttpStatusCode.ServiceUnavailable), // primary attempt 1
            Response(HttpStatusCode.ServiceUnavailable), // primary attempt 2
            Response(HttpStatusCode.ServiceUnavailable), // primary attempt 3 (2 retries + initial => exhausted)
            Response(HttpStatusCode.OK, "fallback-worked"));
        using var http = new HttpClient(handler);

        var result = await GeminiHttpExecutor.SendWithRetryAsync(
            http, Options(), model => new HttpRequestMessage(HttpMethod.Get, $"https://example.test/{model}"), CancellationToken.None, NoDelay);

        Assert.Equal("fallback-worked", result);
        Assert.Equal(4, handler.RequestedUrls.Count);
        Assert.Contains("primary-model", handler.RequestedUrls[0]);
        Assert.Contains("fallback-model", handler.RequestedUrls[^1]);
    }

    [Fact]
    public async Task SendWithRetryAsync_ThrowsAiServiceUnavailable_WhenBothModelsStayDown()
    {
        var handler = new ScriptedHandler(Enumerable.Repeat(0, 6).Select(_ => Response(HttpStatusCode.ServiceUnavailable)).ToArray());
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<AiServiceUnavailableException>(() => GeminiHttpExecutor.SendWithRetryAsync(
            http, Options(), model => new HttpRequestMessage(HttpMethod.Get, $"https://example.test/{model}"), CancellationToken.None, NoDelay));

        Assert.Equal(6, handler.RequestedUrls.Count); // 3 attempts x 2 models
    }

    [Fact]
    public async Task SendWithRetryAsync_DoesNotRetryOrFallBack_OnANonTransientError()
    {
        var handler = new ScriptedHandler(Response(HttpStatusCode.BadRequest, "malformed request"));
        using var http = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => GeminiHttpExecutor.SendWithRetryAsync(
            http, Options(), model => new HttpRequestMessage(HttpMethod.Get, $"https://example.test/{model}"), CancellationToken.None, NoDelay));

        Assert.Contains("400", ex.Message);
        Assert.Single(handler.RequestedUrls); // never retried, never fell back — a bad request stays bad
    }

    [Fact]
    public async Task SendWithRetryAsync_SkipsTheFallbackModel_WhenNoneIsConfigured()
    {
        var handler = new ScriptedHandler(Enumerable.Repeat(0, 3).Select(_ => Response(HttpStatusCode.ServiceUnavailable)).ToArray());
        using var http = new HttpClient(handler);

        await Assert.ThrowsAsync<AiServiceUnavailableException>(() => GeminiHttpExecutor.SendWithRetryAsync(
            http, Options(fallback: null), model => new HttpRequestMessage(HttpMethod.Get, $"https://example.test/{model}"), CancellationToken.None, NoDelay));

        Assert.Equal(3, handler.RequestedUrls.Count); // only the primary model's 3 attempts
        Assert.All(handler.RequestedUrls, url => Assert.Contains("primary-model", url));
    }
}
