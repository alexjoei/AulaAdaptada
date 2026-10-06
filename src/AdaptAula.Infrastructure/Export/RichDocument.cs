namespace AdaptAula.Infrastructure.Export;

/// <summary>Inline formatting of a run of text, as produced by the visual editor (V2 §9).</summary>
public record RunStyle
{
    public bool Bold { get; init; }
    public bool Italic { get; init; }
    public bool Underline { get; init; }
    public bool Strike { get; init; }
    public string? Color { get; init; }
    public string? Highlight { get; init; }
    public double? SizePt { get; init; }
    public string? Font { get; init; }
    public double? LetterSpacingPt { get; init; }
    public double? WordSpacingPt { get; init; }
}

public record TextRun(string Text, RunStyle Style, bool LineBreak = false);

/// <summary>Neutral, renderer-independent model of the editable document. The editor's HTML is parsed into this, and the
/// DOCX and PDF renderers both draw from it — so what the teacher sees is what both formats contain.</summary>
public abstract record DocBlock;

/// <param name="HeadingLevel">0 for a normal paragraph, 1-3 for headings.</param>
/// <param name="ListKind">null, "bullet", "ordered" or "check".</param>
public record ParagraphBlock(
    List<TextRun> Runs, string? Align = null, int HeadingLevel = 0, string? ListKind = null, int ListIndex = 0, int ListDepth = 0,
    double? LineSpacing = null) : DocBlock
{
    public string PlainText => string.Concat(Runs.Select(r => r.LineBreak ? "\n" : r.Text));
}

public record ImageDocBlock(byte[] Bytes, string MimeType, string? Alt, string? Role, double? WidthPx = null) : DocBlock;

public record PageBreakBlock : DocBlock;

public record AnswerSpaceBlock(int Lines, bool Ruled, bool Grid) : DocBlock;

public record TableBlock(List<List<List<DocBlock>>> Rows, bool HeaderRow = false) : DocBlock;

/// <summary>A framed group of blocks — one question, or a shared reading passage.</summary>
public record BoxBlock(List<DocBlock> Children, string Kind, Guid? QuestionId = null, int? Points = null) : DocBlock;
