using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AdaptAula.Tests;

/// <summary>Boots the real API (real controllers, real SQLite file, the offline AI stand-ins) in-process so the tests exercise the same
/// code paths a browser does — nothing mocked below the HTTP boundary.</summary>
public sealed class ApiHarness : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly string _root = Path.Combine(Path.GetTempPath(), "adaptaula-tests-" + Guid.NewGuid().ToString("N"));
    private readonly WebApplicationFactory<Program> _factory;

    public HttpClient Client { get; }

    public ApiHarness()
    {
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable("Ai__Provider", "Offline");
        Environment.SetEnvironmentVariable("Logging__LogLevel__Default", "Warning");
        Environment.SetEnvironmentVariable("ConnectionStrings__Default", $"Data Source={Path.Combine(_root, "test.db")}");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseContentRoot(_root));
        Client = _factory.CreateClient();
    }

    public const string SampleTest =
        "1. Explica la diferencia entre carbohidratos y proteínas. Da un ejemplo de alimento de cada uno. (3 puntos)\n" +
        "2. Calcula el resultado de 25 + 17 y escribe la operación completa. (2 puntos)\n" +
        "3. Describe con tus palabras cómo funciona la fotosíntesis en las plantas verdes. (5 puntos)";

    public async Task<JsonElement> Send(HttpMethod method, string url, object? body = null, HttpStatusCode? expect = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (expect is { } expected)
            Assert.True(response.StatusCode == expected, $"{method} {url} -> {(int)response.StatusCode} (esperado {(int)expected}): {text}");
        else
            Assert.True(response.IsSuccessStatusCode, $"{method} {url} -> {(int)response.StatusCode}: {text}");
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    public Task<JsonElement> Get(string url, HttpStatusCode? expect = null) => Send(HttpMethod.Get, url, null, expect);
    public Task<JsonElement> Post(string url, object? body = null, HttpStatusCode? expect = null) => Send(HttpMethod.Post, url, body ?? new { }, expect);
    public Task<JsonElement> Put(string url, object body, HttpStatusCode? expect = null) => Send(HttpMethod.Put, url, body, expect);

    public async Task<byte[]> PostForBytes(string url, object? body, HttpStatusCode expect = HttpStatusCode.OK)
    {
        using var response = await Client.PostAsJsonAsync(url, body ?? new { }, Json);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        Assert.True(response.StatusCode == expect, $"POST {url} -> {(int)response.StatusCode}: {System.Text.Encoding.UTF8.GetString(bytes)}");
        return bytes;
    }

    public async Task<JsonElement> CreateAssessment(string text = SampleTest, int grade = 4, string subject = "Ciencias")
    {
        var created = await Post("/api/assessments/text", new { title = "Prueba de ciencias", text, grade, subject, language = "es" });
        return created;
    }

    public async Task<JsonElement> CreateProfile(string alias, string[] needs, string[]? accommodations = null, string[]? exceptions = null, object? settings = null) =>
        await Post("/api/profiles", new
        {
            alias, measures = needs, accommodations = accommodations ?? Array.Empty<string>(),
            exceptions = exceptions ?? Array.Empty<string>(), settings = settings ?? new { }
        });

    public async Task<JsonElement> CreateAndGeneratePlan(JsonElement assessment, JsonElement profile, int level = 2)
    {
        var plan = await Post($"/api/assessments/{assessment.GetProperty("id").GetString()}/plans",
            new { profileId = profile.GetProperty("id").GetString(), level });
        var planId = plan.GetProperty("id").GetString();
        await Post($"/api/plans/{planId}/generate");
        return await Get($"/api/plans/{planId}");
    }

    public static List<string> ZipEntries(byte[] zip)
    {
        using var archive = new ZipArchive(new MemoryStream(zip));
        return archive.Entries.Select(e => e.FullName).ToList();
    }

    public void Dispose()
    {
        Client.Dispose();
        _factory.Dispose();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { /* the SQLite file can linger briefly on Windows */ }
    }
}
