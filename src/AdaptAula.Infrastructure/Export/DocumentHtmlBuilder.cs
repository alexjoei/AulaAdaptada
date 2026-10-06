using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AdaptAula.Domain;

namespace AdaptAula.Infrastructure.Export;

/// <summary>One student's row on the teacher sheet (V2 §19 "hoja docente con el resumen de cambios").</summary>
public record TeacherSheetEntry(
    string Alias,
    AdaptationPlan Plan,
    IReadOnlyList<AdaptedQuestion> Adapted,
    IReadOnlyList<ValidationResult> Validation,
    IReadOnlyDictionary<Guid, SemaphoreLevel> Semaphore,
    SemaphoreLevel OverallSemaphore,
    IReadOnlyList<string> AppliedMeasures,
    IReadOnlyList<string> LogisticNotes);

/// <summary>
/// Builds the editable document's HTML (the single source the visual editor shows and the exporters render, V2 §9) and the
/// auxiliary documents — teacher sheet, answer key/rubric, change log — in the same HTML dialect so they all go through
/// the same DOCX/PDF renderers.
/// </summary>
public static class DocumentHtmlBuilder
{
    private static readonly Regex Bold = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);

    public static string Encode(string? text) => WebUtility.HtmlEncode(text ?? string.Empty);

    /// <summary>Encodes plain text and turns **word** into bold — how the AI marks keywords and action verbs.</summary>
    public static string Markup(string? text) => Bold.Replace(Encode(text), "<strong>$1</strong>");

    public static string BuildAdapted(
        Assessment assessment, AdaptationPlan plan,
        IReadOnlyDictionary<Guid, Question> questionsById, IReadOnlyList<AdaptedQuestion> adaptedQuestions)
    {
        var byQuestionId = adaptedQuestions.ToDictionary(a => a.QuestionId);
        return BuildBody(assessment, plan.Style, questionsById, q => byQuestionId.TryGetValue(q.Id, out var a) ? a : null);
    }

    /// <summary>The original test, untouched — "Original" is part of every class pack (V2 §19).</summary>
    public static string BuildOriginal(Assessment assessment)
    {
        var questionsById = assessment.Sections.SelectMany(s => s.Questions).ToDictionary(q => q.Id);
        var originals = questionsById.Values.ToDictionary(
            q => q.Id,
            q => new AdaptedQuestion { QuestionId = q.Id, AdaptedText = q.OriginalText, Points = q.Points });
        return BuildBody(assessment, new DocumentStyle { Align = "left" }, questionsById, q => originals[q.Id]);
    }

    private static string BuildBody(
        Assessment assessment, DocumentStyle style, IReadOnlyDictionary<Guid, Question> questionsById,
        Func<Question, AdaptedQuestion?> adaptedFor)
    {
        var sb = new StringBuilder();
        sb.Append($"<h1>{Encode(assessment.Title)}</h1>");
        sb.Append($"<p><em>{Encode(Subtitle(assessment))}</em></p>");

        var firstQuestion = true;
        foreach (var section in assessment.Sections.OrderBy(s => s.Order))
        {
            var questions = section.Questions.OrderBy(q => q.Order).Where(q => adaptedFor(q) is not null).ToList();
            if (questions.Count == 0) continue;

            if (!string.IsNullOrWhiteSpace(section.StimulusText))
            {
                sb.Append("<div data-type=\"stimulus\">");
                sb.Append("<p><em>Enunciado / texto de referencia</em></p>");
                foreach (var block in ContentBlocks.FromInterleavedText(section.StimulusText, section.AssetRefs))
                {
                    if (block is TextBlock text)
                        foreach (var line in Lines(text.Text)) sb.Append($"<p>{Markup(line)}</p>");
                    else if (block is ImageBlock image && !style.HideDecorativeImages)
                        sb.Append(ImageTag(image, "Imagen del texto de referencia"));
                }
                sb.Append("</div>");
            }

            foreach (var question in questions)
            {
                var adapted = adaptedFor(question)!;

                if (!firstQuestion && style.PageBreakPerQuestion) sb.Append("<div data-type=\"page-break\"></div>");
                firstQuestion = false;

                sb.Append($"<div data-type=\"question\" data-question-id=\"{question.Id}\" data-points=\"{adapted.Points}\">");
                sb.Append($"<h2>Pregunta {question.Order + 1} · {adapted.Points} {(adapted.Points == 1 ? "punto" : "puntos")}</h2>");
                foreach (var line in Lines(adapted.AdaptedText)) sb.Append($"<p>{Markup(line)}</p>");

                if (!style.HideDecorativeImages)
                    foreach (var image in ContentBlocks.FromImageGallery(question.AssetRefs).OfType<ImageBlock>())
                        sb.Append(ImageTag(image, $"Imagen de la pregunta {question.Order + 1}"));

                foreach (var support in adapted.Supports)
                    sb.Append($"<p>{(style.ChecklistSupports ? "□ " : "• ")}{Markup(support)}</p>");

                var lines = AnswerLines(question.Type, style);
                if (lines > 0)
                    sb.Append($"<div data-type=\"answer-space\" data-lines=\"{lines}\" data-ruled=\"{(style.RuledAnswerLines ? "true" : "false")}\" data-grid=\"{(style.GridAnswerSpace ? "true" : "false")}\"></div>");

                sb.Append("</div>");
            }
        }

        return sb.ToString();
    }

    /// <summary>How many answer lines a question type gets by default; scaled by the student's answer-space factor.</summary>
    public static int AnswerLines(QuestionType type, DocumentStyle style)
    {
        var baseLines = type switch
        {
            QuestionType.OpenText => 5,
            QuestionType.ShortAnswer => 2,
            QuestionType.FillInTheBlank => 1,
            QuestionType.Classification => 2,
            _ => 0
        };
        return baseLines == 0 ? 0 : (int)Math.Ceiling(baseLines * Math.Max(1, style.AnswerSpaceFactor));
    }

    private static string Subtitle(Assessment assessment)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(assessment.Subject)) parts.Add(assessment.Subject);
        if (assessment.Grade is not null) parts.Add($"{assessment.Grade}º");
        parts.Add($"{assessment.TotalPoints} puntos");
        return string.Join(" · ", parts);
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0);

    private static string ImageTag(ImageBlock image, string alt) =>
        $"<img src=\"data:{image.MimeType};base64,{Convert.ToBase64String(image.Bytes)}\" alt=\"{Encode(alt)}\" data-role=\"content\">";

    // ---------------------------------------------------------------- auxiliary documents

    public static string BuildTeacherSheet(Assessment assessment, IReadOnlyList<TeacherSheetEntry> entries, IReadOnlyDictionary<Guid, Question> questionsById)
    {
        var sb = new StringBuilder();
        sb.Append($"<h1>Hoja docente · {Encode(assessment.Title)}</h1>");
        sb.Append($"<p><em>{Encode(Subtitle(assessment))} · generado el {DateTime.Now:dd/MM/yyyy}</em></p>");
        sb.Append("<p>Resumen de lo que cambió en cada versión. Documento para uso docente: contiene información educativa sensible, no se entrega al alumnado.</p>");

        foreach (var entry in entries)
        {
            sb.Append($"<h2>{Encode(entry.Alias)} · nivel {entry.Plan.Level} · semáforo: {SemaphoreWord(entry.OverallSemaphore)}</h2>");

            if (entry.Plan.IsCurricularChange)
                sb.Append($"<p><strong>Adaptación curricular activada</strong>{(string.IsNullOrWhiteSpace(entry.Plan.CurricularReference) ? "" : " · referencia: " + Encode(entry.Plan.CurricularReference))}{(string.IsNullOrWhiteSpace(entry.Plan.CurricularObjective) ? "" : " · objetivo: " + Encode(entry.Plan.CurricularObjective))}</p>");

            if (entry.AppliedMeasures.Count > 0)
            {
                sb.Append("<p><strong>Medidas aplicadas</strong></p>");
                foreach (var m in entry.AppliedMeasures) sb.Append($"<p>• {Encode(m)}</p>");
            }

            if (entry.LogisticNotes.Count > 0)
            {
                sb.Append("<p><strong>A tener en cuenta durante la prueba</strong></p>");
                foreach (var n in entry.LogisticNotes) sb.Append($"<p>• {Encode(n)}</p>");
            }

            sb.Append("<table><tr><th>Pregunta</th><th>Semáforo</th><th>Qué cambió</th></tr>");
            foreach (var adapted in entry.Adapted.OrderBy(a => questionsById.TryGetValue(a.QuestionId, out var q) ? q.Order : 0))
            {
                var order = questionsById.TryGetValue(adapted.QuestionId, out var question) ? question.Order + 1 : 0;
                var changes = adapted.ChangeLog.Count == 0
                    ? (adapted.Supports.Count > 0 ? "Apoyos añadidos" : "Sin cambios")
                    : string.Join("; ", adapted.ChangeLog.Select(c => c.Description));
                if (adapted.Proposal is { Status: ProposalStatus.Pending })
                    changes += " · Propuesta pendiente: " + adapted.Proposal.Reason;
                var level = entry.Semaphore.TryGetValue(adapted.QuestionId, out var s) ? s : SemaphoreLevel.Green;
                sb.Append($"<tr><td>{order}</td><td>{SemaphoreWord(level)}</td><td>{Encode(changes)}</td></tr>");
            }
            sb.Append("</table>");

            var findings = entry.Validation.Where(v => v.Severity != ValidationSeverity.Warning || v.Code != ValidationCode.DifficultyReduced).ToList();
            if (findings.Count > 0)
            {
                sb.Append("<p><strong>Avisos de validación</strong></p>");
                foreach (var v in findings) sb.Append($"<p>• [{SeverityWord(v.Severity)}] {Encode(v.Message)}</p>");
            }
        }

        return sb.ToString();
    }

    public static string BuildAnswerKey(Assessment assessment, IReadOnlyDictionary<string, string>? criteriaTexts = null)
    {
        var sb = new StringBuilder();
        sb.Append($"<h1>Rúbrica y solucionario · {Encode(assessment.Title)}</h1>");
        sb.Append($"<p><em>{Encode(Subtitle(assessment))}. Es la misma para todas las versiones: la adaptación nunca cambia respuestas ni puntuación.</em></p>");
        sb.Append("<table><tr><th>Pregunta</th><th>Puntos</th><th>Respuesta esperada</th><th>Criterios</th></tr>");
        foreach (var question in assessment.Sections.OrderBy(s => s.Order).SelectMany(s => s.Questions.OrderBy(q => q.Order)))
        {
            var criteria = question.Analysis is null
                ? ""
                : string.Join("; ", question.Analysis.CriteriaIds.Select(id => criteriaTexts is not null && criteriaTexts.TryGetValue(id, out var t) ? $"{id} {t}" : id));
            sb.Append($"<tr><td>{question.Order + 1}</td><td>{question.Points}</td><td>{Encode(question.ExpectedAnswer ?? "—")}</td><td>{Encode(criteria)}</td></tr>");
        }
        sb.Append($"<tr><td><strong>Total</strong></td><td><strong>{assessment.TotalPoints}</strong></td><td></td><td></td></tr>");
        sb.Append("</table>");
        return sb.ToString();
    }

    public static string BuildChangeLogSheet(Assessment assessment, string alias, IReadOnlyList<ChangeLogRecord> records, IReadOnlyDictionary<Guid, Question> questionsById)
    {
        var sb = new StringBuilder();
        sb.Append($"<h1>Historial de cambios · {Encode(assessment.Title)}</h1>");
        sb.Append($"<p><em>{Encode(alias)}</em></p>");
        sb.Append("<table><tr><th>Fecha</th><th>Pregunta</th><th>Medida</th><th>Antes</th><th>Después</th><th>Motivo</th><th>Riesgo</th><th>Decisión</th></tr>");
        foreach (var r in records.OrderBy(r => r.At))
        {
            var order = r.QuestionId is { } id && questionsById.TryGetValue(id, out var q) ? (q.Order + 1).ToString() : "—";
            sb.Append($"<tr><td>{r.At.ToLocalTime():dd/MM HH:mm}</td><td>{order}</td><td>{Encode(r.RuleId)}</td><td>{Encode(Trim(r.Before))}</td><td>{Encode(Trim(r.After))}</td><td>{Encode(r.Reason)}</td><td>{r.Risk}</td><td>{DecisionWord(r.Decision)}</td></tr>");
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    private static string Trim(string value) => value.Length > 120 ? value[..120] + "…" : value;

    public static string SemaphoreWord(SemaphoreLevel level) => level switch
    {
        SemaphoreLevel.Green => "verde",
        SemaphoreLevel.Orange => "naranja",
        _ => "rojo"
    };

    private static string SeverityWord(ValidationSeverity severity) => severity switch
    {
        ValidationSeverity.Error => "Error",
        ValidationSeverity.Review => "Revisión",
        _ => "Aviso"
    };

    private static string DecisionWord(TeacherDecision decision) => decision switch
    {
        TeacherDecision.Accepted => "aceptado",
        TeacherDecision.Rejected => "rechazado",
        TeacherDecision.Edited => "editado",
        _ => "pendiente"
    };

    // ---------------------------------------------------------------- accessible digital version

    /// <summary>A self-contained, semantic HTML page (V2 §20 "versión digital accesible"): language attribute, landmarks,
    /// skip link, readable type driven by the student's own style, keyboard-friendly and screen-reader friendly.</summary>
    public static string BuildAccessiblePage(string title, string language, string bodyHtml, DocumentStyle style)
    {
        var textColor = style.HighContrast ? "#000" : "#1a1a1a";
        return $$"""
            <!doctype html>
            <html lang="{{Encode(language)}}">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>{{Encode(title)}}</title>
            <style>
              body { font-family: {{Encode(style.FontFamily)}}, Arial, sans-serif; font-size: {{style.FontSizePt.ToString(System.Globalization.CultureInfo.InvariantCulture)}}pt; line-height: {{style.LineSpacing.ToString(System.Globalization.CultureInfo.InvariantCulture)}}; letter-spacing: {{style.LetterSpacingPt.ToString(System.Globalization.CultureInfo.InvariantCulture)}}pt; word-spacing: {{style.WordSpacingPt.ToString(System.Globalization.CultureInfo.InvariantCulture)}}pt; color: {{textColor}}; background: #fff; max-width: 46rem; margin: 0 auto; padding: 1rem; }
              .skip { position: absolute; left: -999px; } .skip:focus { left: 0; background: #fff; padding: .5rem; }
              h1, h2 { line-height: 1.25; } [data-type=question] { border: 2px solid #444; border-radius: 6px; padding: .75rem 1rem; margin: 1.25rem 0; }
              [data-type=answer-space] { min-height: 4rem; border-bottom: 2px dotted #666; margin-top: .75rem; }
              img { max-width: 100%; height: auto; } table { border-collapse: collapse; } td, th { border: 1px solid #444; padding: .3rem .5rem; }
              :focus-visible { outline: 3px solid #0050d0; outline-offset: 2px; }
            </style>
            </head>
            <body>
            <a class="skip" href="#contenido">Saltar al contenido</a>
            <main id="contenido" tabindex="-1">
            {{bodyHtml}}
            </main>
            </body>
            </html>
            """;
    }
}
