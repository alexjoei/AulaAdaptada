using System.Text.Json.Serialization;

namespace AdaptAula.Infrastructure.Ai;

/// <summary>AI tools a teacher can run on a selected piece of text (V2 §10).</summary>
public enum TextTool
{
    Shorten,
    SimplifySyntax,
    SplitIntoSteps,
    HighlightKeywords,
    AddExample,
    WordBank,
    Hint,
    Checklist,
    Organizer,
    ReadAloud
}

public record TextToolRequest(
    TextTool Tool,
    string Text,
    string Language,
    string? QuestionContext,
    IReadOnlyList<string> ProtectedVocabulary);

/// <param name="Original">What the teacher selected.</param>
/// <param name="Proposal">What would replace it — shown "Original → Propuesta" before anything is applied.</param>
/// <param name="Note">Short explanation of what changed.</param>
public record TextToolResult(string Original, string Proposal, string Note);

public interface ITextToolService
{
    Task<TextToolResult> RunAsync(TextToolRequest request, CancellationToken ct = default);
}

public static class TextToolInfo
{
    /// <summary>Tools that add content the student could use to answer, so their output is checked against the expected answer.</summary>
    public static bool AddsHelp(TextTool tool) => tool is TextTool.AddExample or TextTool.WordBank or TextTool.Hint or TextTool.Checklist or TextTool.Organizer;

    /// <summary>Tools that can make the question easier, so the teacher is told to double-check what is being assessed.</summary>
    public static bool MayReduceDemand(TextTool tool) => tool is TextTool.Shorten or TextTool.SimplifySyntax or TextTool.Hint or TextTool.WordBank;
}

internal class GeminiTextToolPayload
{
    [JsonPropertyName("proposal")]
    public string Proposal { get; set; } = string.Empty;

    [JsonPropertyName("note")]
    public string Note { get; set; } = string.Empty;
}

/// <summary>Gemini-backed implementation. <see cref="TextTool.ReadAloud"/> needs no AI: the browser's speech synthesis reads the text.</summary>
public class GeminiTextToolService : ITextToolService
{
    private readonly GeminiJsonClient _client;

    public GeminiTextToolService(GeminiJsonClient client) => _client = client;

    public async Task<TextToolResult> RunAsync(TextToolRequest request, CancellationToken ct = default)
    {
        if (request.Tool == TextTool.ReadAloud)
            return new TextToolResult(request.Text, request.Text, "Se leerá en voz alta tal cual; el texto no cambia.");

        var system = SystemPrompt + "\nHERRAMIENTA: " + Instruction(request.Tool);
        var user = new System.Text.StringBuilder();
        user.AppendLine($"Idioma: {request.Language}");
        if (!string.IsNullOrWhiteSpace(request.QuestionContext))
        {
            user.AppendLine("CONTEXTO (la pregunta completa, solo para que entiendas el fragmento; NO la reescribas):");
            user.AppendLine(request.QuestionContext);
        }
        if (request.ProtectedVocabulary.Count > 0)
            user.AppendLine($"VOCABULARIO PROTEGIDO (debe seguir apareciendo literalmente si estaba): {string.Join(", ", request.ProtectedVocabulary)}");
        user.AppendLine("TEXTO SELECCIONADO:");
        user.AppendLine(request.Text);

        var payload = await _client.GenerateAsync<GeminiTextToolPayload>(system, user.ToString(), Schema, ct);
        return new TextToolResult(request.Text, payload.Proposal, payload.Note);
    }

    private const string SystemPrompt = """
        ROL: Eres un asistente para docentes que adapta un fragmento de una prueba escolar. Solo transformas el TEXTO SELECCIONADO.
        REGLAS: (1) Conserva el significado evaluado, el idioma y el vocabulario protegido. (2) Nunca reveles ni insinúes la respuesta correcta. (3) No inventes datos. (4) Devuelve solo el texto que sustituiría a la selección en "proposal" (no repitas el contexto) y, en "note", una frase corta sobre qué hiciste. (5) Para destacar palabras usa **negrita** con asteriscos dobles. (6) Cada paso o elemento de lista en su propia línea.
        """;

    private static string Instruction(TextTool tool) => tool switch
    {
        TextTool.Shorten => "Acorta el texto manteniendo todas las ideas necesarias para responder; elimina solo lo redundante.",
        TextTool.SimplifySyntax => "Simplifica la sintaxis: frases más cortas y directas, sin cambiar el vocabulario curricular ni lo que se pide.",
        TextTool.SplitIntoSteps => "Divide la instrucción en pasos numerados (1., 2., 3.), uno por línea, una acción por paso.",
        TextTool.HighlightKeywords => "Devuelve el mismo texto con las palabras clave y los verbos de acción en **negrita**. No cambies ninguna otra palabra.",
        TextTool.AddExample => "Añade debajo un ejemplo de FORMATO de respuesta con un caso distinto al de la pregunta; nunca el caso de la pregunta ni su solución.",
        TextTool.WordBank => "Añade un banco de palabras útiles para redactar la respuesta, mezclando palabras relevantes e irrelevantes; sin incluir la respuesta correcta completa.",
        TextTool.Hint => "Añade una pista breve que oriente el razonamiento sin dar la respuesta ni los datos que hay que obtener.",
        TextTool.Checklist => "Añade una lista de comprobación breve (3 a 5 elementos) para revisar la respuesta, sin contenido de la solución.",
        TextTool.Organizer => "Añade un organizador sencillo (por ejemplo, una tabla o esquema de huecos con rótulos) para estructurar la respuesta, sin rellenarlo.",
        _ => "Devuelve el texto sin cambios."
    };

    private static readonly object Schema = new
    {
        type = "OBJECT",
        properties = new { proposal = new { type = "STRING" }, note = new { type = "STRING" } },
        required = new[] { "proposal", "note" }
    };
}
