using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AdaptAula.Infrastructure.Ai;

/// <summary>
/// INGEST/PARSE step (spec §7). Gemini reads the source document directly — its real text and
/// layout — and returns the question structure as JSON. This replaces the old PdfPig-text-position-
/// reconstruction + regex heuristics: those broke on every layout quirk a human hadn't anticipated
/// (decorative graphics mistaken for content, option runs misdetected, stems misattributed across
/// question boundaries), because they had no actual understanding of the document. Reading the
/// document the way a person would is what generalizes.
///
/// Embedded images are deliberately NOT matched by asking Gemini to visually compare extracted image
/// crops against the document — that was tried (as separately-labeled reference images sent
/// alongside the PDF, both combined with structuring and as its own dedicated follow-up call) and
/// proved unreliable regardless of prompt or model tier: verified against a real 15-page, 36-
/// question, ~25-image assessment, only a handful of images ever got matched. The model is excellent
/// at reading a document's text/structure but not at that kind of cross-referencing task. Instead,
/// each section reports the page range its content spans (read directly off the same document it's
/// already parsing reliably), and <see cref="AdaptAula.Infrastructure.Ingestion.DocumentIngestionService"/>
/// matches each embedded image to whichever section's page range covers the page it came from — a
/// deterministic, page-number equality check instead of an AI guess.
/// </summary>
public class GeminiDocumentStructureExtractor : IDocumentStructureExtractor
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly GeminiOptions _options;

    public GeminiDocumentStructureExtractor(HttpClient http, IOptions<GeminiOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public Task<DocumentStructure> ExtractFromPdfAsync(byte[] pdfBytes, CancellationToken ct = default)
    {
        var parts = new List<GeminiPart>
        {
            GeminiPart.FromText("Documento completo (PDF) a continuación."),
            GeminiPart.FromBytes(pdfBytes, "application/pdf")
        };
        return ExtractAsync(parts, includesPageNumbers: true, ct);
    }

    public Task<DocumentStructure> ExtractFromTextAsync(string text, CancellationToken ct = default)
    {
        var parts = new List<GeminiPart>
        {
            GeminiPart.FromText("Texto completo de la prueba a continuación:"),
            GeminiPart.FromText(text)
        };
        return ExtractAsync(parts, includesPageNumbers: false, ct);
    }

    private async Task<DocumentStructure> ExtractAsync(List<GeminiPart> parts, bool includesPageNumbers, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException(
                "Gemini API key not configured. Set it via configuration key 'Gemini:ApiKey' or the GEMINI_API_KEY environment variable.");

        var body = new GeminiRequest
        {
            SystemInstruction = new GeminiContent { Parts = { GeminiPart.FromText(includesPageNumbers ? PdfSystemPrompt : TextSystemPrompt) } },
            Contents = { new GeminiContent { Role = "user", Parts = parts } },
            GenerationConfig = new GeminiGenerationConfig { ResponseSchema = includesPageNumbers ? PdfResponseSchema : TextResponseSchema }
        };
        var json = JsonSerializer.Serialize(body, JsonOptions);

        var responseBody = await GeminiHttpExecutor.SendWithRetryAsync(
            _http,
            _options,
            model => new HttpRequestMessage(HttpMethod.Post, $"{_options.BaseUrl}/models/{model}:generateContent?key={Uri.EscapeDataString(_options.ApiKey)}")
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            },
            ct);

        var parsed = JsonSerializer.Deserialize<GeminiResponse>(responseBody, JsonOptions)
            ?? throw new InvalidOperationException("Empty response from Gemini.");

        var text = parsed.Candidates.FirstOrDefault()?.Content?.Parts.FirstOrDefault()?.Text
            ?? throw new InvalidOperationException("Gemini response had no candidates.");

        var payload = JsonSerializer.Deserialize<GeminiDocumentStructurePayload>(text, JsonOptions)
            ?? throw new InvalidOperationException("Could not parse Gemini's structured JSON output.");

        return new DocumentStructure(
            payload.Sections.Select(s => new ExtractedSection(
                string.IsNullOrWhiteSpace(s.StimulusText) ? null : s.StimulusText,
                new List<int>(),
                s.Questions.Select(q => new ExtractedQuestion(q.Text, q.Options, q.PointsHint, new List<int>())).ToList(),
                s.StartPage,
                s.EndPage
            )).ToList(),
            Math.Clamp(payload.Confidence, 0, 1));
    }

    private const string SharedRules = """
        (1) Ignora SOLO las instrucciones genéricas de la prueba en su conjunto (cómo rellenar el examen, cómo marcar o corregir una respuesta, portadas) y cualquier ejercicio de ejemplo ya resuelto ("Mira este ejemplo", "Ejercicio de ejemplo", "Sample question") — no son preguntas reales y no deben aparecer en la salida. NO ignores el título de una lectura/actividad (p. ej. "Fun with caution") ni una instrucción específica de esa lectura o sección (p. ej. "Look at this information. Read the text carefully and answer the questions."): esas SÍ son necesarias para entender qué hay que hacer con esa sección concreta, e incluyéndolas al principio de "stimulus_text".
        (2) Cuando varias preguntas compartan un mismo texto de lectura, contexto o imagen (p. ej. una comprensión lectora), agrúpalas en una misma sección con ese texto en "stimulus_text", en vez de repetirlo en cada pregunta.
        (3) Copia el enunciado EXACTAMENTE como aparece en el documento — no corrijas, resumas, traduzcas ni parafrasees nada. Para "options", devuelve SOLO el texto de cada opción, SIN la letra o número que la precede en el documento (si el documento muestra "A. Friday", el valor debe ser "Friday"; la letra se añade automáticamente después y duplicarla es un error).
        (4) Para preguntas de verdadero/falso con una tabla de afirmaciones, incluye cada afirmación completa en su propia línea dentro de "text", en el mismo orden del documento.
        (5) Si el texto compartido de una sección incluye una tabla de datos (precios, cantidades, porcentajes u otra información en filas/columnas, no solo verdadero/falso), transcribe su contenido completo como texto legible dentro de "stimulus_text" (una fila por línea, incluyendo sus valores) en vez de omitirlo por formar parte de un gráfico o tabla visual — es necesario para responder a las preguntas sobre esa sección.
        (6) Si el documento indica explícitamente la puntuación de una pregunta, ponla en "points_hint"; si no se indica, omite ese campo.
        """;

    private const string TextSystemPrompt = $"""
        ROL: Eres un motor de extracción de estructura para evaluaciones educativas (en español o inglés).
        OBJETIVO: leer el documento proporcionado (una prueba/examen) y devolver su estructura en preguntas, preservando el contenido original.
        REGLAS:
        {SharedRules}
        (7) Devuelve en "confidence" un valor entre 0 y 1 que refleje tu seguridad en esta extracción.
        SALIDA: JSON estructurado según el esquema proporcionado, sin texto adicional fuera del JSON.
        """;

    private const string PdfSystemPrompt = $"""
        ROL: Eres un motor de extracción de estructura para evaluaciones educativas (en español o inglés).
        OBJETIVO: leer el documento proporcionado (una prueba/examen) y devolver su estructura en preguntas, preservando el contenido original.
        REGLAS:
        {SharedRules}
        (7) Indica en "start_page" y "end_page" el número de página (empezando en 1, tal como se numeran las páginas físicas del PDF en orden) donde comienza y termina el contenido de cada sección: su texto compartido (si existe) y todas sus preguntas. Si la sección cabe en una sola página, "start_page" y "end_page" son iguales.
        (8) Cuando una foto, gráfico, diagrama o tabla visual aparezca intercalado DENTRO del texto compartido de una sección (por ejemplo, entre dos párrafos de una lectura, junto a un globo de diálogo, o junto a un dato concreto), inserta la marca literal [IMG] en "stimulus_text" exactamente en el punto de lectura donde aparece esa imagen, en el mismo orden en que se leerían. No la resumas ni la describas: solo marca su posición con [IMG]. No inventes marcas para elementos puramente decorativos (logotipos, iconos de navegación, números de pregunta estilizados). IMPORTANTE: nunca agrupes varias marcas [IMG] seguidas al final del texto — eso rompe la asociación entre cada imagen y la frase que ilustra. Cada [IMG] va pegada, en su propia posición, justo después de la frase o dato concreto al que esa imagen concreta acompaña en el documento original, aunque eso signifique poner una marca [IMG] en mitad de un párrafo. Ejemplo correcto: "Me llamo Susan y vivo en Banff. [IMG] También me encanta esquiar en las montañas. [IMG] Hay que tener cuidado con los osos. [IMG]" — NO: "Me llamo Susan... también me encanta esquiar... hay que tener cuidado con los osos. [IMG] [IMG] [IMG]".
        (9) Devuelve en "confidence" un valor entre 0 y 1 que refleje tu seguridad en esta extracción.
        SALIDA: JSON estructurado según el esquema proporcionado, sin texto adicional fuera del JSON.
        """;

    private static readonly object QuestionSchema = new
    {
        type = "OBJECT",
        properties = new
        {
            text = new { type = "STRING" },
            options = new { type = "ARRAY", items = new { type = "STRING" } },
            points_hint = new { type = "INTEGER", nullable = true }
        },
        required = new[] { "text" }
    };

    private static readonly object TextResponseSchema = new
    {
        type = "OBJECT",
        properties = new
        {
            confidence = new { type = "NUMBER" },
            sections = new
            {
                type = "ARRAY",
                items = new
                {
                    type = "OBJECT",
                    properties = new { stimulus_text = new { type = "STRING", nullable = true }, questions = new { type = "ARRAY", items = QuestionSchema } },
                    required = new[] { "questions" }
                }
            }
        },
        required = new[] { "sections", "confidence" }
    };

    private static readonly object PdfResponseSchema = new
    {
        type = "OBJECT",
        properties = new
        {
            confidence = new { type = "NUMBER" },
            sections = new
            {
                type = "ARRAY",
                items = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        stimulus_text = new { type = "STRING", nullable = true },
                        start_page = new { type = "INTEGER" },
                        end_page = new { type = "INTEGER" },
                        questions = new { type = "ARRAY", items = QuestionSchema }
                    },
                    required = new[] { "questions", "start_page", "end_page" }
                }
            }
        },
        required = new[] { "sections", "confidence" }
    };
}
