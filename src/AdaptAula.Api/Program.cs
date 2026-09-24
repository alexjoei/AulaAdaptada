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
        .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
        .AllowAnyHeader()
        .AllowAnyMethod());
});

var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=App_Data/adaptaula.db";
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "App_Data"));
builder.Services.AddDbContext<AdaptAulaDbContext>(options => options.UseSqlite(connectionString));

builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.AddHttpClient<IAdaptationTextGenerator, GeminiAdaptationTextGenerator>();

builder.Services.AddSingleton<DocumentIngestionService>();
builder.Services.AddSingleton<DocxExporter>();
builder.Services.AddSingleton<PdfExporter>();
builder.Services.AddScoped<AdaptationPipelineService>();
builder.Services.AddScoped<ExportService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AdaptAulaDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AdaptaulaWeb");
app.UseAuthorization();
app.MapControllers();

app.Run();
