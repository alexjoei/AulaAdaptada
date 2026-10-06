using System.Globalization;
using AdaptAula.Domain;

namespace AdaptAula.RulesEngine;

/// <summary>A non-text measure the teacher/centre must carry out (extra time, scribe, braille copy…) — shown on the
/// teacher sheet and never sent to the AI.</summary>
public record LogisticNote(string RuleId, string Text);

/// <summary>
/// Turns the style measures that apply to a plan into one <see cref="DocumentStyle"/>. Individual parameter values
/// ("&lt;ruleId&gt;.&lt;param&gt;" in the profile settings) beat the measure's default, and when several measures touch the
/// same property the more accessible value wins (largest size, largest spacing, left alignment…).
/// </summary>
public static class DocumentStyleResolver
{
    public static DocumentStyle Resolve(IEnumerable<AdaptationRule> appliedRules, IReadOnlyDictionary<string, string> settings)
    {
        var style = new DocumentStyle();
        var fontChosen = false;

        foreach (var rule in appliedRules.OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            foreach (var (key, defaultValue) in rule.Style)
            {
                var value = settings.TryGetValue($"{rule.Id}.{key}", out var custom) && !string.IsNullOrWhiteSpace(custom)
                    ? custom
                    : defaultValue;
                Apply(style, key, value, ref fontChosen);
            }
        }

        return style;
    }

    private static void Apply(DocumentStyle style, string key, string value, ref bool fontChosen)
    {
        switch (key)
        {
            case "fontFamily":
                if (!fontChosen && !string.IsNullOrWhiteSpace(value)) { style.FontFamily = value; fontChosen = true; }
                break;
            case "fontSizePt": style.FontSizePt = Math.Max(style.FontSizePt, Num(value, style.FontSizePt)); break;
            case "lineSpacing": style.LineSpacing = Math.Max(style.LineSpacing, Num(value, style.LineSpacing)); break;
            case "letterSpacingPt": style.LetterSpacingPt = Math.Max(style.LetterSpacingPt, Num(value, 0)); break;
            case "wordSpacingPt": style.WordSpacingPt = Math.Max(style.WordSpacingPt, Num(value, 0)); break;
            case "paragraphSpacingPt": style.ParagraphSpacingPt = Math.Max(style.ParagraphSpacingPt, Num(value, style.ParagraphSpacingPt)); break;
            case "marginMm": style.MarginMm = Math.Max(style.MarginMm, Num(value, style.MarginMm)); break;
            case "answerSpaceFactor": style.AnswerSpaceFactor = Math.Max(style.AnswerSpaceFactor, Num(value, 1)); break;
            case "align": if (value == "left") style.Align = "left"; break;
            case "pageBreakPerQuestion": style.PageBreakPerQuestion |= Bool(value); break;
            case "highContrast": style.HighContrast |= Bool(value); break;
            case "checklistSupports": style.ChecklistSupports |= Bool(value); break;
            case "ruledAnswerLines": style.RuledAnswerLines |= Bool(value); break;
            case "gridAnswerSpace": style.GridAnswerSpace |= Bool(value); break;
            case "hideDecorativeImages": style.HideDecorativeImages |= Bool(value); break;
        }
    }

    private static double Num(string value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : fallback;

    private static bool Bool(string value) => value.Equals("true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Human-readable lines for Logistics measures, with the student's own parameter values filled in.</summary>
    public static List<LogisticNote> LogisticNotes(IEnumerable<AdaptationRule> appliedRules, IReadOnlyDictionary<string, string> settings)
    {
        var notes = new List<LogisticNote>();
        foreach (var rule in appliedRules.Where(r => r.Kind == MeasureKind.Logistics).OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            var parts = rule.Parameters.Select(p =>
            {
                var value = settings.TryGetValue($"{rule.Id}.{p.Key}", out var custom) && !string.IsNullOrWhiteSpace(custom) ? custom : p.Default;
                return $"{p.Label}: {value}{(string.IsNullOrEmpty(p.Unit) ? "" : " " + p.Unit)}";
            }).ToList();

            notes.Add(new LogisticNote(rule.Id, parts.Count == 0 ? rule.Description : $"{rule.Description} ({string.Join(", ", parts)})"));
        }
        return notes;
    }
}
