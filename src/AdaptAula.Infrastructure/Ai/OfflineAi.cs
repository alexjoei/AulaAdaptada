using System.Text.RegularExpressions;
using AdaptAula.Domain;

namespace AdaptAula.Infrastructure.Ai;

// "Modo sin IA" (Ai:Provider = Offline): deterministic, rule-based stand-ins for the Gemini services. They need no API key and
// no network, so the whole app — ingestion, adaptation, comparator, editor, packs, exports — can be demoed and tested end to
// end. They are deliberately simple (not a replacement for the model) but follow the same contracts, including proposals and
// protected vocabulary.

internal static class OfflineText
{
    private static readonly string[] ActionVerbs =
    {
        "explica", "calcula", "escribe", "subraya", "une", "completa", "resuelve", "señala", "indica", "justifica", "describe",
        "nombra", "dibuja", "ordena", "clasifica", "responde", "lee", "relaciona", "marca", "elige", "compara", "identifica", "enumera"
    };

    public static List<string> Sentences(string text) =>
        Regex.Split(text.Trim(), @"(?<=[\.\?\!])\s+").Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

    public static string BoldVerbs(string text)
    {
        foreach (var verb in ActionVerbs)
            text = Regex.Replace(text, $@"\b({verb})\b", "**$1**", RegexOptions.IgnoreCase);
        return text.Replace("****", "**");
    }

    public static string Numbered(string text)
    {
        var sentences = Sentences(text);
        return sentences.Count < 2 ? text : string.Join("\n", sentences.Select((s, i) => $"{i + 1}. {s}"));
    }

    public static string LongestWord(string text) =>
        Regex.Matches(text, @"\p{L}{6,}").Select(m => m.Value).OrderByDescending(w => w.Length).FirstOrDefault() ?? string.Empty;

    public static string Shorten(string text, IReadOnlyList<string> protectedTerms)
    {
        var sentences = Sentences(text);
        var kept = sentences.Take(Math.Max(1, sentences.Count / 2)).ToList();
        var result = string.Join(" ", kept);
        foreach (var term in protectedTerms.Where(t => text.Contains(t, StringComparison.OrdinalIgnoreCase) && !result.Contains(t, StringComparison.OrdinalIgnoreCase)))
            result += $" ({term})";
        return result;
    }
}

public class OfflineAdaptationTextGenerator : IAdaptationTextGenerator
{
    public Task<IReadOnlyList<AdaptationTextResponse>> GenerateBatchAsync(
        IReadOnlyList<AdaptationTextRequest> requests, CancellationToken ct = default)
    {
        IReadOnlyList<AdaptationTextResponse> responses = requests.Select(Adapt).ToList();
        return Task.FromResult(responses);
    }

    private static AdaptationTextResponse Adapt(AdaptationTextRequest request)
    {
        var original = request.Question.OriginalText;
        var text = original;
        var supports = new List<string>();
        var changes = new List<ChangeLogEntry>();
        var protectedTerms = request.Protected?.ProtectedVocabulary ?? Array.Empty<string>();

        void Log(string ruleId, string description, string before, string after) =>
            changes.Add(new ChangeLogEntry { RuleId = ruleId, Description = description, Before = before, After = after, Category = request.AppliedRules.First(r => r.RuleId == ruleId).Category });

        foreach (var rule in request.AppliedRules.Where(r => !r.ProposalOnly))
        {
            var before = text;
            switch (rule.RuleId)
            {
                case "adhd.one_action_per_block":
                case "tdl_tel.split_multi_instructions":
                case "language.one_idea_per_sentence":
                case "adhd.fragment_tasks":
                    text = OfflineText.Numbered(text);
                    if (text != before) Log(rule.RuleId, "Instrucción dividida en pasos numerados", before, text);
                    break;
                case "adhd.highlight_action_verbs":
                case "dyslexia.short_statements_keywords":
                    text = OfflineText.BoldVerbs(text);
                    if (text != before) Log(rule.RuleId, "Verbos de acción destacados en negrita", before, text);
                    break;
                case "asd.task_step_count":
                    var steps = OfflineText.Sentences(original).Count;
                    text = $"Esta pregunta tiene {steps} {(steps == 1 ? "paso" : "pasos")}.\n{text}";
                    Log(rule.RuleId, "Se indica el número de pasos", before, text);
                    break;
                case "adhd.progress_checklist":
                case "executive_functions.checklist_steps_progress":
                    supports.AddRange(new[] { "Leo el enunciado", "Hago lo que pide", "Reviso mi respuesta" });
                    Log(rule.RuleId, "Checklist de progreso añadido", string.Empty, "Leo / Hago / Reviso");
                    break;
                case "attention.self_check_box":
                    supports.Add("He revisado mi respuesta");
                    Log(rule.RuleId, "Casilla de autorrevisión añadida", string.Empty, "He revisado mi respuesta");
                    break;
                case "tdl_tel.glossary_visual_support":
                case "reading_comprehension.anticipate_vocabulary":
                case "spanish_l2.visual_glossary_clear_language":
                    var word = OfflineText.LongestWord(original);
                    if (word.Length > 0)
                    {
                        supports.Add($"Palabra clave: {word}");
                        Log(rule.RuleId, "Glosario breve añadido", string.Empty, $"Palabra clave: {word}");
                    }
                    break;
                case "visual.pictograms":
                    var key = OfflineText.LongestWord(original);
                    if (key.Length > 0)
                    {
                        supports.Add($"Pictograma sugerido para «{key}»");
                        Log(rule.RuleId, "Pictograma sugerido", string.Empty, key);
                    }
                    break;
                case "attention.step_by_step_plan":
                    supports.Add("Antes de empezar: ¿qué me piden? ¿qué necesito? ¿cómo lo hago?");
                    Log(rule.RuleId, "Plan previo en pasos", string.Empty, "Plan de pasos");
                    break;
                default:
                    break;
            }
        }

        AdaptationProposal? proposal = null;
        var proposalRules = request.AppliedRules.Where(r => r.ProposalOnly).ToList();
        if (proposalRules.Count > 0)
        {
            var proposed = OfflineText.Shorten(text, protectedTerms);
            if (proposed.Trim() != text.Trim())
                proposal = new AdaptationProposal(proposed, proposalRules.Select(r => r.RuleId).ToList(),
                    "Acorta o simplifica el enunciado; podría rebajar lo que se evalúa, por eso se propone sin aplicar.");
        }

        return new AdaptationTextResponse(
            request.Question.Id, text, ResponseMode.Written, supports, changes, new List<string>(), proposal);
    }
}

public class OfflineTextToolService : ITextToolService
{
    public Task<TextToolResult> RunAsync(TextToolRequest request, CancellationToken ct = default)
    {
        var text = request.Text;
        var (proposal, note) = request.Tool switch
        {
            TextTool.Shorten => (OfflineText.Shorten(text, request.ProtectedVocabulary), "Se quedó con la primera mitad del texto."),
            TextTool.SimplifySyntax => (text.Replace(", ", ". ").Replace("; ", ". "), "Frases más cortas."),
            TextTool.SplitIntoSteps => (OfflineText.Numbered(text), "Dividido en pasos numerados."),
            TextTool.HighlightKeywords => (OfflineText.BoldVerbs(text), "Verbos de acción en negrita."),
            TextTool.AddExample => (text + "\nEjemplo de formato: Respuesta: ____ porque ____.", "Ejemplo de formato añadido."),
            TextTool.WordBank => (text + "\nBanco de palabras: " + string.Join(", ", Regex.Matches(text, @"\p{L}{5,}").Select(m => m.Value.ToLowerInvariant()).Distinct().Take(4)), "Banco de palabras añadido."),
            TextTool.Hint => (text + "\nPista: relee el enunciado y subraya los datos.", "Pista añadida."),
            TextTool.Checklist => (text + "\n☐ Leo el enunciado\n☐ Respondo\n☐ Reviso", "Checklist añadido."),
            TextTool.Organizer => (text + "\nIdea principal: ____\nEjemplo: ____", "Organizador añadido."),
            _ => (text, "Sin cambios.")
        };
        return Task.FromResult(new TextToolResult(text, proposal, note));
    }
}

public class OfflineQuestionAnalyzer : IQuestionAnalyzer
{
    public Task<IReadOnlyList<(Guid QuestionId, QuestionAnalysis Analysis)>> AnalyzeAsync(
        IReadOnlyList<QuestionToAnalyze> questions, IReadOnlyList<AnalysisCandidate> criteria, IReadOnlyList<AnalysisCandidate> contents,
        string areaName, string language, CancellationToken ct = default)
    {
        IReadOnlyList<(Guid, QuestionAnalysis)> results = questions.Select(q => (q.QuestionId, Analyze(q, criteria, contents))).ToList();
        return Task.FromResult(results);
    }

    private static QuestionAnalysis Analyze(QuestionToAnalyze q, IReadOnlyList<AnalysisCandidate> criteria, IReadOnlyList<AnalysisCandidate> contents)
    {
        var lower = q.Text.ToLowerInvariant();
        var demand = lower.Contains("justifica") || lower.Contains("compara") || lower.Contains("por qué") ? "analizar"
            : lower.Contains("calcula") || lower.Contains("resuelve") ? "aplicar"
            : lower.Contains("explica") || lower.Contains("describe") ? "comprender"
            : "recordar";
        var words = q.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        return new QuestionAnalysis
        {
            Content = q.Text.Length > 90 ? q.Text[..90] + "…" : q.Text,
            Skill = demand switch { "aplicar" => "Aplicar procedimientos", "analizar" => "Razonar y argumentar", "comprender" => "Comprender y explicar", _ => "Recordar y reconocer" },
            CognitiveDemand = demand,
            LinguisticDemand = words > 30 ? "alta" : words > 12 ? "media" : "baja",
            ReadingLoad = words > 30 ? "alta" : words > 12 ? "media" : "baja",
            WritingLoad = q.Type is QuestionType.OpenText ? "alta" : q.Type is QuestionType.ShortAnswer or QuestionType.FillInTheBlank ? "media" : "baja",
            ExecutiveLoad = words > 25 ? "media" : "baja",
            CriteriaIds = BestMatches(q.Text, criteria, 2),
            ContentIds = BestMatches(q.Text, contents, 1),
            Source = "ai",
            Confirmed = false
        };
    }

    private static List<string> BestMatches(string text, IReadOnlyList<AnalysisCandidate> candidates, int take)
    {
        var words = Regex.Matches(text.ToLowerInvariant(), @"\p{L}{5,}").Select(m => m.Value).ToHashSet();
        return candidates
            .Select(c => (c.Id, Score: Regex.Matches(c.Text.ToLowerInvariant(), @"\p{L}{5,}").Count(m => words.Contains(m.Value))))
            .Where(x => x.Score > 0)
            .OrderByDescending(x => x.Score).Take(take).Select(x => x.Id).ToList();
    }
}

public class OfflineDocumentStructureExtractor : IDocumentStructureExtractor
{
    private static readonly Regex QuestionStart = new(@"^\s*(\d{1,3})\s*[\.\)\-]\s+(.*)$", RegexOptions.Compiled);
    private static readonly Regex PointsHint = new(@"\((\d+)\s*(?:puntos?|pts?)\)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public Task<DocumentStructure> ExtractFromPdfAsync(byte[] pdfBytes, CancellationToken ct = default) =>
        throw new InvalidOperationException("El modo sin IA no puede leer PDF; pega el texto o sube un DOCX.");

    public Task<DocumentStructure> ExtractFromTextAsync(string text, CancellationToken ct = default)
    {
        var questions = new List<ExtractedQuestion>();
        var current = new List<string>();

        void Flush()
        {
            if (current.Count == 0) return;
            var joined = string.Join("\n", current).Trim();
            var options = current.Skip(1).Where(l => Regex.IsMatch(l, @"^\s*[a-dA-D][\)\.]\s+")).Select(l => l.Trim()).ToList();
            var match = PointsHint.Match(joined);
            questions.Add(new ExtractedQuestion(
                PointsHint.Replace(joined, string.Empty).Trim(), options, match.Success ? int.Parse(match.Groups[1].Value) : null, new List<int>()));
            current.Clear();
        }

        foreach (var line in text.Split('\n'))
        {
            var m = QuestionStart.Match(line);
            if (m.Success) { Flush(); current.Add(m.Groups[2].Value); }
            else if (current.Count > 0 && !string.IsNullOrWhiteSpace(line)) current.Add(line.Trim());
        }
        Flush();

        if (questions.Count == 0)
            questions.Add(new ExtractedQuestion(text.Trim(), new List<string>(), null, new List<int>()));

        return Task.FromResult(new DocumentStructure(
            new List<ExtractedSection> { new(null, new List<int>(), questions) }, questions.Count > 1 ? 0.9 : 0.4));
    }
}
