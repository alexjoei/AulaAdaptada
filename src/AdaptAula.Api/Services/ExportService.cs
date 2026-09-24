using System.Security.Cryptography;
using AdaptAula.Domain;
using AdaptAula.Infrastructure.Export;
using AdaptAula.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AdaptAula.Api.Services;

public class ExportBlockedException : Exception
{
    public ExportBlockedException(string message) : base(message) { }
}

/// <summary>EXPORT step (spec §7/§12): renders the adapted document and records the version +
/// approval, but only once <see cref="SafetyValidator"/> reports no unresolved ERROR — enforced
/// again here as the last gate before anything leaves the system.</summary>
public class ExportService
{
    private readonly AdaptAulaDbContext _db;
    private readonly DocxExporter _docxExporter;
    private readonly PdfExporter _pdfExporter;
    private readonly string _exportRoot;

    public ExportService(AdaptAulaDbContext db, DocxExporter docxExporter, PdfExporter pdfExporter, IWebHostEnvironment env)
    {
        _db = db;
        _docxExporter = docxExporter;
        _pdfExporter = pdfExporter;
        _exportRoot = Path.Combine(env.ContentRootPath, "App_Data", "exports");
        Directory.CreateDirectory(_exportRoot);
    }

    public async Task<ExportVersion> ExportAsync(
        Assessment assessment, AdaptationPlan plan, List<AdaptedQuestion> adaptedQuestions,
        ExportFormat format, string approvedBy, CancellationToken ct)
    {
        var unresolvedErrors = await _db.ValidationResults
            .Where(v => v.PlanId == plan.Id && v.Severity == ValidationSeverity.Error)
            .ToListAsync(ct);

        if (unresolvedErrors.Count > 0)
        {
            throw new ExportBlockedException(
                $"No se puede exportar: hay {unresolvedErrors.Count} error(es) de validación sin resolver.");
        }

        if (plan.IsCurricularChange && string.IsNullOrWhiteSpace(approvedBy))
        {
            throw new ExportBlockedException(
                "Cambio curricular: requiere aprobación explícita del docente (nombre/alias de quien aprueba) antes de exportar.");
        }

        var questionsById = assessment.Sections.SelectMany(s => s.Questions).ToDictionary(q => q.Id);

        var bytes = format == ExportFormat.Docx
            ? _docxExporter.Export(assessment, plan, questionsById, adaptedQuestions)
            : _pdfExporter.Export(assessment, plan, questionsById, adaptedQuestions);

        var extension = format == ExportFormat.Docx ? "docx" : "pdf";
        var fileName = $"{assessment.Id}_{plan.Id}_{DateTime.UtcNow:yyyyMMddHHmmss}.{extension}";
        var filePath = Path.Combine(_exportRoot, fileName);
        await File.WriteAllBytesAsync(filePath, bytes, ct);

        var version = new ExportVersion
        {
            AssessmentId = assessment.Id,
            ProfileId = plan.ProfileId,
            PlanId = plan.Id,
            Format = format,
            ApprovedBy = approvedBy,
            Hash = Convert.ToHexString(SHA256.HashData(bytes)),
            FilePath = filePath
        };

        _db.ExportVersions.Add(version);
        plan.Status = PlanStatus.Exported;
        await _db.SaveChangesAsync(ct);

        return version;
    }
}
