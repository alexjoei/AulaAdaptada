using System.IO.Compression;
using AdaptAula.Domain;
using AdaptAula.Infrastructure.Export;
using AdaptAula.Infrastructure.Persistence;
using AdaptAula.RulesEngine;
using AdaptAula.Validation;
using Microsoft.EntityFrameworkCore;

namespace AdaptAula.Api.Services;

public record ExportBundleOptions(
    bool Pdf = true,
    bool Docx = true,
    bool IncludeOriginal = true,
    bool TeacherSheet = true,
    bool AnswerKey = false,
    bool ChangeLog = false,
    bool AccessibleHtml = false,
    string ApprovedBy = "");

public class BundleBlockedException : Exception
{
    public List<string> Reasons { get; }

    public BundleBlockedException(List<string> reasons) : base("No se puede exportar el pack: " + string.Join(" | ", reasons)) => Reasons = reasons;
}

/// <summary>
/// Builds the ZIP a teacher takes to class (V2 §19, §20): original + one version per student, each as print-ready PDF and
/// editable Word, plus — optionally — the teacher sheet with the summary of changes, the answer key/rubric, each student's
/// change history and an accessible digital version. Every version is validated again here; nothing leaves with an unresolved error.
/// </summary>
public class PackExportService
{
    private readonly AdaptAulaDbContext _db;
    private readonly ExportService _export;
    private readonly PlanDocumentService _documents;
    private readonly CurriculumService _curriculum;
    private readonly ChangeLogWriter _changeLog;

    public PackExportService(
        AdaptAulaDbContext db, ExportService export, PlanDocumentService documents, CurriculumService curriculum, ChangeLogWriter changeLog)
    {
        _db = db;
        _export = export;
        _documents = documents;
        _curriculum = curriculum;
        _changeLog = changeLog;
    }

    public async Task<byte[]> BuildAsync(IReadOnlyList<PlanAssessment> plans, ExportBundleOptions options, CancellationToken ct)
    {
        if (plans.Count == 0) throw new BundleBlockedException(new List<string> { "No hay versiones que exportar." });
        if (!options.Pdf && !options.Docx && !options.AccessibleHtml)
            throw new BundleBlockedException(new List<string> { "Elige al menos un formato (PDF, Word o versión digital)." });

        var aliases = await _db.StudentProfiles.ToDictionaryAsync(p => p.Id, p => p.Alias, ct);

        // Gate every version first: a pack is all-or-nothing so a student never silently misses their copy.
        var blocked = new List<string>();
        var validations = new Dictionary<Guid, List<ValidationResult>>();
        foreach (var ctx in plans)
        {
            var alias = aliases.GetValueOrDefault(ctx.Plan.ProfileId, "alumno");
            if (ctx.Adapted.Count == 0) { blocked.Add($"{alias}: todavía no se han generado los textos adaptados."); continue; }
            try
            {
                validations[ctx.Plan.Id] = await _export.EnsureExportableAsync(ctx, options.ApprovedBy, ct);
            }
            catch (ExportBlockedException ex)
            {
                blocked.Add($"{alias}: {ex.Message}");
            }
        }
        if (blocked.Count > 0) throw new BundleBlockedException(blocked);

        var assessment = plans[0].Assessment;
        var questionsById = PlanDocumentService.QuestionsById(assessment);

        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(string name, byte[] bytes)
            {
                var unique = name;
                for (var i = 2; !used.Add(unique); i++)
                    unique = $"{Path.GetFileNameWithoutExtension(name)}_{i}{Path.GetExtension(name)}";
                var entry = zip.CreateEntry(unique, CompressionLevel.Optimal);
                using var stream = entry.Open();
                stream.Write(bytes, 0, bytes.Length);
            }

            void AddRendered(string baseName, string html, DocumentStyle style, string? footer = null)
            {
                if (options.Pdf) Add($"{baseName}.pdf", _export.Render(html, style, ExportFormat.Pdf, footer));
                if (options.Docx) Add($"{baseName}.docx", _export.Render(html, style, ExportFormat.Docx));
            }

            var title = ExportService.Slugify(assessment.Title);
            var number = 1;

            if (options.IncludeOriginal)
                AddRendered($"{number++:00}_Original_{title}", DocumentHtmlBuilder.BuildOriginal(assessment), new DocumentStyle { Align = "left" },
                    "Aula Adaptada · prueba original");

            foreach (var ctx in plans)
            {
                var alias = ExportService.Slugify(aliases.GetValueOrDefault(ctx.Plan.ProfileId, "alumno"));
                var html = PlanDocumentService.CurrentHtml(ctx);
                var prefix = $"{number++:00}_{alias}";

                AddRendered($"{prefix}_{title}", html, ctx.Plan.Style);
                if (options.AccessibleHtml)
                    Add($"{prefix}_{title}_version_digital.html", System.Text.Encoding.UTF8.GetBytes(
                        DocumentHtmlBuilder.BuildAccessiblePage(assessment.Title, assessment.Language, html, ctx.Plan.Style)));

                if (options.ChangeLog)
                {
                    var records = await _db.ChangeLogRecords.Where(c => c.PlanId == ctx.Plan.Id).ToListAsync(ct);
                    AddRendered($"Historial_{alias}", DocumentHtmlBuilder.BuildChangeLogSheet(assessment, aliases.GetValueOrDefault(ctx.Plan.ProfileId, "alumno"), records, questionsById),
                        new DocumentStyle { FontSizePt = 9 }, "Aula Adaptada · historial de cambios, uso docente");
                }
            }

            if (options.TeacherSheet)
            {
                var entries = plans.Select(ctx => BuildEntry(ctx, aliases, validations[ctx.Plan.Id])).ToList();
                AddRendered("Hoja_docente", DocumentHtmlBuilder.BuildTeacherSheet(assessment, entries, questionsById),
                    new DocumentStyle { FontSizePt = 10 }, "Aula Adaptada · hoja docente, no entregar al alumnado");
            }

            if (options.AnswerKey)
                AddRendered("Rubrica_solucionario", DocumentHtmlBuilder.BuildAnswerKey(assessment, _curriculum.CriteriaTexts()),
                    new DocumentStyle { FontSizePt = 10 }, "Aula Adaptada · solucionario, no entregar al alumnado");
        }

        foreach (var ctx in plans)
        {
            ctx.Plan.Status = PlanStatus.Exported;
            _changeLog.Add(ctx.Plan.Id, null, "export", string.Empty, string.Empty, "pack",
                "Incluido en un pack exportado.", RiskLevel.Low, TeacherDecision.Accepted, options.ApprovedBy);
        }
        await _db.SaveChangesAsync(ct);

        return buffer.ToArray();
    }

    private TeacherSheetEntry BuildEntry(PlanAssessment ctx, IReadOnlyDictionary<Guid, string> aliases, List<ValidationResult> validation)
    {
        var semaphore = _documents.ComputeSemaphore(ctx, validation);
        Func<string, AdaptationRule?> lookup = id => RuleCatalog.ById.TryGetValue(id, out var r) ? r : ctx.ExtraRules.GetValueOrDefault(id);

        var applied = ctx.Plan.ResolvedRulesByQuestion.Values.SelectMany(l => l).Where(r => r.Applied).Select(r => r.RuleId).Distinct()
            .Select(lookup).Where(r => r is not null).Select(r => r!).ToList();

        return new TeacherSheetEntry(
            aliases.GetValueOrDefault(ctx.Plan.ProfileId, "alumno"), ctx.Plan, ctx.Adapted, validation,
            semaphore.Questions.ToDictionary(q => q.QuestionId, q => q.Level), semaphore.Overall,
            applied.Where(r => r.Kind != MeasureKind.Logistics).Select(r => r.Description).ToList(),
            DocumentStyleResolver.LogisticNotes(applied, ctx.Plan.ProfileSettings).Select(n => n.Text).ToList());
    }
}
