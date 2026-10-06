using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace AdaptAula.Infrastructure.Export;

/// <summary>
/// Parses the visual editor's HTML into the renderer-independent <see cref="DocBlock"/> model. Deliberately tolerant:
/// unknown tags are treated as plain containers and unparseable styles are ignored, so one odd paste never fails an export.
/// Custom blocks are plain <c>div[data-type=…]</c> elements so the HTML round-trips through the editor unchanged.
/// </summary>
public static class RichDocumentParser
{
    private static readonly Regex StyleDecl = new(@"([\w-]+)\s*:\s*([^;]+)", RegexOptions.Compiled);
    private static readonly Regex Number = new(@"-?\d+(\.\d+)?", RegexOptions.Compiled);

    public static List<DocBlock> Parse(string? html)
    {
        var blocks = new List<DocBlock>();
        if (string.IsNullOrWhiteSpace(html)) return blocks;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        ParseChildren(doc.DocumentNode, blocks, new ListContext());
        return blocks;
    }

    private record ListContext(string? Kind = null, int Depth = 0);

    private static void ParseChildren(HtmlNode parent, List<DocBlock> blocks, ListContext list)
    {
        var inlineBuffer = new List<TextRun>();

        void FlushInline()
        {
            if (inlineBuffer.Any(r => r.LineBreak || !string.IsNullOrWhiteSpace(r.Text)))
                blocks.Add(new ParagraphBlock(new List<TextRun>(inlineBuffer)));
            inlineBuffer.Clear();
        }

        foreach (var node in parent.ChildNodes)
        {
            if (node.NodeType == HtmlNodeType.Text)
            {
                var text = HtmlEntity.DeEntitize(node.InnerText);
                if (!string.IsNullOrWhiteSpace(text)) inlineBuffer.Add(new TextRun(text, new RunStyle()));
                continue;
            }
            if (node.NodeType != HtmlNodeType.Element) continue;

            switch (node.Name.ToLowerInvariant())
            {
                case "p":
                case "h1": case "h2": case "h3": case "h4": case "h5": case "h6":
                    FlushInline();
                    blocks.Add(ParseParagraph(node, list));
                    break;

                case "ul":
                case "ol":
                    FlushInline();
                    ParseList(node, blocks, list);
                    break;

                case "blockquote":
                    FlushInline();
                    ParseChildren(node, blocks, list);
                    break;

                case "hr":
                    FlushInline();
                    blocks.Add(new PageBreakBlock());
                    break;

                case "img":
                    FlushInline();
                    if (ParseImage(node) is { } image) blocks.Add(image);
                    break;

                case "table":
                    FlushInline();
                    blocks.Add(ParseTable(node));
                    break;

                case "div":
                case "section":
                    FlushInline();
                    ParseDiv(node, blocks, list);
                    break;

                case "br":
                    inlineBuffer.Add(new TextRun(string.Empty, new RunStyle(), LineBreak: true));
                    break;

                case "script":
                case "style":
                    break;

                default:
                    // Inline element sitting directly in a container (strong/em/span/a…).
                    CollectRuns(node, new RunStyle(), inlineBuffer);
                    break;
            }
        }

        FlushInline();
    }

    private static void ParseDiv(HtmlNode node, List<DocBlock> blocks, ListContext list)
    {
        var type = node.GetAttributeValue("data-type", string.Empty);
        switch (type)
        {
            case "page-break":
                blocks.Add(new PageBreakBlock());
                return;

            case "answer-space":
                blocks.Add(new AnswerSpaceBlock(
                    Math.Clamp(node.GetAttributeValue("data-lines", 3), 0, 60),
                    node.GetAttributeValue("data-ruled", "false") == "true",
                    node.GetAttributeValue("data-grid", "false") == "true"));
                return;

            case "question":
            case "stimulus":
            case "block":
            {
                var children = new List<DocBlock>();
                ParseChildren(node, children, list);
                Guid? questionId = Guid.TryParse(node.GetAttributeValue("data-question-id", string.Empty), out var id) ? id : null;
                int? points = int.TryParse(node.GetAttributeValue("data-points", string.Empty), out var p) ? p : null;
                blocks.Add(new BoxBlock(children, type, questionId, points));
                return;
            }

            default:
                ParseChildren(node, blocks, list);
                return;
        }
    }

    private static void ParseList(HtmlNode node, List<DocBlock> blocks, ListContext parent)
    {
        var kind = node.Name.Equals("ol", StringComparison.OrdinalIgnoreCase) ? "ordered" : "bullet";
        var context = new ListContext(kind, parent.Depth + (parent.Kind is null ? 0 : 1));
        var index = 1;

        foreach (var item in node.ChildNodes.Where(n => n.Name.Equals("li", StringComparison.OrdinalIgnoreCase)))
        {
            // An <li> usually wraps a <p>; flatten it into one list paragraph and recurse for nested lists.
            var runs = new List<TextRun>();
            string? align = null;
            var nested = new List<HtmlNode>();
            foreach (var child in item.ChildNodes)
            {
                var name = child.Name.ToLowerInvariant();
                if (name is "ul" or "ol") { nested.Add(child); continue; }
                if (name == "p")
                {
                    if (runs.Count > 0) runs.Add(new TextRun(string.Empty, new RunStyle(), LineBreak: true));
                    CollectRuns(child, new RunStyle(), runs);
                    align ??= ParseStyle(child).GetValueOrDefault("text-align");
                    continue;
                }
                CollectRuns(child, new RunStyle(), runs);
            }

            blocks.Add(new ParagraphBlock(runs, align, 0, kind, index++, context.Depth));
            foreach (var n in nested) ParseList(n, blocks, context);
        }
    }

    private static ParagraphBlock ParseParagraph(HtmlNode node, ListContext list)
    {
        var heading = node.Name.Length == 2 && node.Name[0] is 'h' or 'H' && char.IsDigit(node.Name[1]) ? node.Name[1] - '0' : 0;
        var styles = ParseStyle(node);
        var runs = new List<TextRun>();
        CollectRuns(node, new RunStyle(), runs);
        // The editor writes line-height on an inline span as well as on the paragraph itself; either way it spaces the whole paragraph.
        var lineHeight = styles.GetValueOrDefault("line-height")
            ?? node.Descendants("span").Select(s => ParseStyle(s).GetValueOrDefault("line-height")).FirstOrDefault(v => v is not null);
        double? lineSpacing = lineHeight is not null && TryNumber(lineHeight, out var lhValue) && lhValue > 0 && lhValue < 5 ? lhValue : null;
        return new ParagraphBlock(runs, styles.GetValueOrDefault("text-align"), Math.Min(heading, 3), list.Kind, 0, list.Depth, lineSpacing);
    }

    private static void CollectRuns(HtmlNode node, RunStyle inherited, List<TextRun> runs)
    {
        if (node.NodeType == HtmlNodeType.Text)
        {
            var text = HtmlEntity.DeEntitize(node.InnerText);
            if (text.Length > 0) runs.Add(new TextRun(text, inherited));
            return;
        }
        if (node.NodeType != HtmlNodeType.Element && node.NodeType != HtmlNodeType.Document) return;

        var style = inherited;
        switch (node.Name.ToLowerInvariant())
        {
            case "br":
                runs.Add(new TextRun(string.Empty, inherited, LineBreak: true));
                return;
            case "strong": case "b": style = style with { Bold = true }; break;
            case "em": case "i": style = style with { Italic = true }; break;
            case "u": style = style with { Underline = true }; break;
            case "s": case "strike": case "del": style = style with { Strike = true }; break;
            case "mark":
                style = style with { Highlight = ParseColor(node.GetAttributeValue("data-color", string.Empty)) ?? ParseColor(ParseStyle(node).GetValueOrDefault("background-color")) ?? "FFFF00" };
                break;
            case "img": case "script": case "style": return;
        }

        var declared = ParseStyle(node);
        if (declared.Count > 0)
        {
            if (declared.TryGetValue("color", out var color) && ParseColor(color) is { } c) style = style with { Color = c };
            if (declared.TryGetValue("background-color", out var bg) && ParseColor(bg) is { } h) style = style with { Highlight = h };
            if (declared.TryGetValue("font-size", out var size) && ParseSizePt(size) is { } pt) style = style with { SizePt = pt };
            if (declared.TryGetValue("font-family", out var family) && !string.IsNullOrWhiteSpace(family))
                style = style with { Font = family.Split(',')[0].Trim().Trim('"', '\'') };
            if (declared.TryGetValue("letter-spacing", out var ls) && ParseSizePt(ls) is { } lsPt) style = style with { LetterSpacingPt = lsPt };
            if (declared.TryGetValue("word-spacing", out var ws) && ParseSizePt(ws) is { } wsPt) style = style with { WordSpacingPt = wsPt };
            if (declared.TryGetValue("font-weight", out var weight) && (weight == "bold" || (int.TryParse(weight, out var w) && w >= 600))) style = style with { Bold = true };
            if (declared.TryGetValue("font-style", out var fs) && fs == "italic") style = style with { Italic = true };
            if (declared.TryGetValue("text-decoration", out var td) && td.Contains("underline")) style = style with { Underline = true };
        }

        foreach (var child in node.ChildNodes) CollectRuns(child, style, runs);
    }

    private static ImageDocBlock? ParseImage(HtmlNode node)
    {
        var src = node.GetAttributeValue("src", string.Empty);
        if (!TryParseDataUri(src, out var bytes, out var mime)) return null;

        double? width = null;
        var widthAttr = node.GetAttributeValue("width", string.Empty);
        if (TryNumber(widthAttr, out var w)) width = w;
        else if (ParseStyle(node).TryGetValue("width", out var cssWidth) && cssWidth.Contains("px") && TryNumber(cssWidth, out var cw)) width = cw;

        var role = node.GetAttributeValue("data-role", string.Empty);
        return new ImageDocBlock(bytes, mime, node.GetAttributeValue("alt", string.Empty), string.IsNullOrEmpty(role) ? null : role, width);
    }

    private static TableBlock ParseTable(HtmlNode node)
    {
        var rows = new List<List<List<DocBlock>>>();
        var header = false;
        foreach (var tr in node.Descendants("tr"))
        {
            var cells = new List<List<DocBlock>>();
            foreach (var cell in tr.ChildNodes.Where(n => n.Name is "td" or "th"))
            {
                if (cell.Name == "th" && rows.Count == 0) header = true;
                var content = new List<DocBlock>();
                ParseChildren(cell, content, new ListContext());
                if (content.Count == 0) content.Add(new ParagraphBlock(new List<TextRun>()));
                cells.Add(content);
            }
            if (cells.Count > 0) rows.Add(cells);
        }
        return new TableBlock(rows, header);
    }

    public static Dictionary<string, string> ParseStyle(HtmlNode node)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var style = node.GetAttributeValue("style", string.Empty);
        if (style.Length == 0) return result;
        foreach (Match m in StyleDecl.Matches(style)) result[m.Groups[1].Value.Trim()] = m.Groups[2].Value.Trim();
        return result;
    }

    private static bool TryNumber(string value, out double number)
    {
        number = 0;
        var m = Number.Match(value ?? string.Empty);
        return m.Success && double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
    }

    /// <summary>CSS length → points. px×0.75, pt as is, em/rem×11 (a sensible body size to scale from).</summary>
    private static double? ParseSizePt(string value)
    {
        if (!TryNumber(value, out var n)) return null;
        var v = value.ToLowerInvariant();
        if (v.Contains("px")) return n * 0.75;
        if (v.Contains("em")) return n * 11;
        return n;
    }

    /// <summary>"#rrggbb", "#rgb" or "rgb(r,g,b)" → "RRGGBB"; null when unusable.</summary>
    public static string? ParseColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        value = value.Trim();
        if (value.StartsWith('#'))
        {
            var hex = value[1..];
            if (hex.Length == 3) hex = string.Concat(hex.Select(ch => $"{ch}{ch}"));
            return hex.Length == 6 && hex.All(Uri.IsHexDigit) ? hex.ToUpperInvariant() : null;
        }
        if (value.StartsWith("rgb", StringComparison.OrdinalIgnoreCase))
        {
            var nums = Number.Matches(value).Select(m => (int)Math.Round(double.Parse(m.Value, CultureInfo.InvariantCulture))).Take(3).ToList();
            if (nums.Count == 3) return string.Concat(nums.Select(x => Math.Clamp(x, 0, 255).ToString("X2")));
        }
        return null;
    }

    public static bool TryParseDataUri(string dataUri, out byte[] bytes, out string mimeType)
    {
        bytes = Array.Empty<byte>();
        mimeType = string.Empty;
        if (string.IsNullOrEmpty(dataUri) || !dataUri.StartsWith("data:", StringComparison.Ordinal)) return false;
        var comma = dataUri.IndexOf(',');
        if (comma < 0) return false;
        var header = dataUri[5..comma];
        var semicolon = header.IndexOf(';');
        if (semicolon < 0) return false;
        mimeType = header[..semicolon];
        try
        {
            bytes = Convert.FromBase64String(dataUri[(comma + 1)..]);
            return bytes.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
