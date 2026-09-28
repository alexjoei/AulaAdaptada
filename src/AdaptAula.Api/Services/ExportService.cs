using System.Security.Cryptography;
using System.Text.RegularExpressions;
using AdaptAula.Domain;
using AdaptAula.Infrastructure.Export;
using AdaptAula.Infrastructure.Persistence;
using AdaptAula.RulesEngine;
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
        var profile = await _db.StudentProfiles.FindAsync(new object[] { plan.ProfileId }, ct);
        var fileName = BuildFileName(assessment, profile, extension);

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

    private static string Slugify(string value) => UnsafeFileNameChars.Replace(value.Trim(), "_").Trim('_');
}
