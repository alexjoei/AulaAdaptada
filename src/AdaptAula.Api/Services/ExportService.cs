using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AdaptAula.Domain;
using AdaptAula.Infrastructure.Export;
using AdaptAula.Infrastructure.Persistence;
using AdaptAula.RulesEngine;
using AdaptAula.Validation;
using Microsoft.EntityFrameworkCore;

namespace AdaptAula.Api.Services;

public class ExportBlockedException : Exception
{
    public ExportBlockedException(string message) : base(message) { }
}

/// <summary>EXPORT step (spec §7/§12): renders the document the teacher approved and records the version + approval,
/// but only once <see cref="SafetyValidator"/> reports no unresolved ERROR — enforced again here, on a fresh validation of
/// the document as it stands now, as the last gate before anything leaves the system (V2 §17).</summary>
public class ExportService
{
    private readonly AdaptAulaDbContext _db;
    private readonly DocxExporter _docxExporter;
    private readonly PdfExporter _pdfExporter;
    private readonly PlanDocumentService _documents;
    private readonly ChangeLogWriter _changeLog;
    private readonly string _exportRoot;

    public ExportService(
        AdaptAulaDbContext db, DocxExporter docxExporter, PdfExporter pdfExporter, PlanDocumentService documents,
        ChangeLogWriter changeLog, IWebHostEnvironment env)
    {
        _db = db;
        _docxExporter = docxExporter;
        _pdfExporter = pdfExporter;
        _documents = documents;
        _changeLog = changeLog;
        _exportRoot = Path.Combine(env.ContentRootPath, "App_Data", "exports");
        Directory.CreateDirectory(_exportRoot);
    }

    /// <summary>Blocks the export on any unresolved Error and returns the (fresh) validation results.
    /// Anything the validator flags for review — and every curricular change — needs a named approver.</summary>
    public async Task<List<ValidationResult>> EnsureExportableAsync(PlanAssessment ctx, string approvedBy, CancellationToken ct)
    {
        var (validation, _) = await _documents.RevalidateAndSaveAsync(ctx, ct);

        var errors = validation.Count(v => v.Severity == ValidationSeverity.Error);
        if (errors > 0)
            throw new ExportBlockedException($"No se puede exportar: hay {errors} error(es) de validación sin resolver.");

        if (ctx.Plan.IsCurricularChange && string.IsNullOrWhiteSpace(approvedBy))
            throw new ExportBlockedException(
                "Cambio curricular: requiere aprobación explícita del docente (nombre/alias de quien aprueba) antes de exportar.");

        if (validation.Any(v => v.Severity == ValidationSeverity.Review) && string.IsNullOrWhiteSpace(approvedBy))
            throw new ExportBlockedException("Hay puntos marcados para revisión: indica quién aprueba la versión antes de exportar.");

        return validation;
    }

    public byte[] Render(string html, DocumentStyle style, ExportFormat format, string? footer = null)
    {
        var blocks = RichDocumentParser.Parse(html);
        return format == ExportFormat.Docx
            ? _docxExporter.Render(blocks, style)
            : footer is null ? _pdfExporter.Render(blocks, style) : _pdfExporter.Render(blocks, style, footer);
    }

    public async Task<ExportVersion> ExportAsync(PlanAssessment ctx, ExportFormat format, string approvedBy, CancellationToken ct)
    {
        await EnsureExportableAsync(ctx, approvedBy, ct);

        var bytes = Render(PlanDocumentService.CurrentHtml(ctx), ctx.Plan.Style, format);

        var extension = format == ExportFormat.Docx ? "docx" : "pdf";
        var profile = await _db.StudentProfiles.FindAsync(new object[] { ctx.Plan.ProfileId }, ct);
        var fileName = BuildFileName(ctx.Assessment, profile, extension);

        // Each export version gets its own folder so the download name can stay purely descriptive
        // (grade-subject-language-accommodations-original) without a uniqueness suffix baked in —
        // uniqueness on disk comes from the folder, not the filename itself.
        var versionId = Guid.NewGuid();
        var versionDir = Path.Combine(_exportRoot, versionId.ToString());
        Directory.CreateDirectory(versionDir);
        var filePath = Path.Combine(versionDir, fileName);
        await File.WriteAllBytesAsync(filePath, bytes, ct);

        var version = new ExportVersion
        {
            Id = versionId,
            AssessmentId = ctx.Assessment.Id,
            ProfileId = ctx.Plan.ProfileId,
            PlanId = ctx.Plan.Id,
            Format = format,
            ApprovedBy = approvedBy,
            Hash = Convert.ToHexString(SHA256.HashData(bytes)),
            FilePath = filePath
        };

        _db.ExportVersions.Add(version);
        ctx.Plan.Status = PlanStatus.Exported;
        _changeLog.Add(ctx.Plan.Id, null, "export", string.Empty, string.Empty, fileName,
            $"Exportado en {extension.ToUpperInvariant()}", RiskLevel.Low, TeacherDecision.Accepted, approvedBy);
        await _db.SaveChangesAsync(ct);

        return version;
    }

    /// <summary>Curso-asignatura-idioma-(adaptaciones)-nombre_original, skipping whatever the
    /// teacher didn't specify (grade/subject are optional) rather than showing an empty segment.</summary>
    internal static string BuildFileName(Assessment assessment, StudentProfile? profile, string extension)
    {
        var parts = new List<string>();
        if (assessment.Grade is not null) parts.Add(assessment.Grade.Value.ToString());
        if (!string.IsNullOrWhiteSpace(assessment.Subject)) parts.Add(Slugify(assessment.Subject));
        if (!string.IsNullOrWhiteSpace(assessment.Language)) parts.Add(assessment.Language);

        var accommodationLabels = (profile?.Measures ?? new List<string>())
            .Select(key => NecessityPresets.ByKey.TryGetValue(key, out var preset) ? preset.DisplayName : key)
            .Select(Slugify)
            .ToList();
        if (accommodationLabels.Count > 0) parts.Add(string.Join("+", accommodationLabels));

        var originalNameBase = !string.IsNullOrWhiteSpace(assessment.SourceFileName)
            ? Path.GetFileNameWithoutExtension(assessment.SourceFileName)
            : assessment.Title;
        if (!string.IsNullOrWhiteSpace(originalNameBase)) parts.Add(Slugify(originalNameBase));

        var baseName = parts.Count > 0 ? string.Join("-", parts) : "adaptaula-export";
        return $"{baseName}.{extension}";
    }

    private static readonly Regex UnsafeFileNameChars = new(@"[^\w\-]+", RegexOptions.Compiled);

    internal static string Slugify(string value) => UnsafeFileNameChars.Replace(value.Trim(), "_").Trim('_');
}
