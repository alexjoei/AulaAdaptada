using AdaptAula.Api.Services;
using AdaptAula.Infrastructure.Ai;
using AdaptAula.Infrastructure.Export;
using AdaptAula.Infrastructure.Ingestion;
using AdaptAula.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

QuestPDF.Settings.License = LicenseType.Community;

builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AdaptaulaWeb", policy => policy
        .WithOrigins(
            "http://localhost:5173",
            "http://127.0.0.1:5173",
            "https://agzlabs.com",
            "https://www.agzlabs.com")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=App_Data/adaptaula.db";
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
builder.Services.AddDbContext<AdaptAulaDbContext>(options => options.UseSqlite(connectionString));

builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));

// Ai:Provider = "Offline" swaps the Gemini services for deterministic rule-based stand-ins (no API key, no network) so the
// whole app can be demoed and tested end to end. Anything else (the default) uses Gemini.
if (string.Equals(builder.Configuration["Ai:Provider"], "Offline", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IAdaptationTextGenerator, OfflineAdaptationTextGenerator>();
    builder.Services.AddSingleton<IDocumentStructureExtractor, OfflineDocumentStructureExtractor>();
    builder.Services.AddSingleton<ITextToolService, OfflineTextToolService>();
    builder.Services.AddSingleton<IQuestionAnalyzer, OfflineQuestionAnalyzer>();
}
else
{
    builder.Services.AddHttpClient<IAdaptationTextGenerator, GeminiAdaptationTextGenerator>();
    builder.Services.AddHttpClient<IDocumentStructureExtractor, GeminiDocumentStructureExtractor>();
    builder.Services.AddHttpClient<GeminiJsonClient>();
    builder.Services.AddTransient<ITextToolService, GeminiTextToolService>();
    builder.Services.AddTransient<IQuestionAnalyzer, GeminiQuestionAnalyzer>();
}

builder.Services.AddSingleton<DocumentIngestionService>();
builder.Services.AddSingleton<DocxExporter>();
builder.Services.AddSingleton<PdfExporter>();
builder.Services.AddSingleton<GenerationProgressTracker>();
builder.Services.AddScoped<AdaptationPipelineService>();
builder.Services.AddSingleton<CurriculumService>();
builder.Services.AddScoped<PlanDocumentService>();
builder.Services.AddScoped<ChangeLogWriter>();
builder.Services.AddScoped<ExportService>();
builder.Services.AddScoped<PackExportService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AdaptAulaDbContext>();
    SchemaUpgrader.Upgrade(db);
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Wraps every request so an AI-service outage (Gemini 503/429, retries and fallback model already
// exhausted by GeminiHttpExecutor) reaches the teacher as a clear, actionable message instead of a
// raw stack trace — everything else still falls through to the default (dev exception page, or a
// bare 500 in production) unchanged.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (AiServiceUnavailableException ex)
    {
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        await context.Response.WriteAsJsonAsync(new { error = ex.Message, code = "ai_unavailable" });
    }
});

app.UseCors("AdaptaulaWeb");
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program { }
