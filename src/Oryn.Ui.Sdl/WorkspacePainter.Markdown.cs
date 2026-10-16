using System.Globalization;
using Oryn.App;
using SkiaSharp;

namespace Oryn.Ui.Sdl;

public sealed partial class WorkspacePainter
{
    private readonly Dictionary<MarkdownStyle, SKFont> _markdownFonts = [];
    private SKFont MarkdownFont(MarkdownStyle style)
    {
        if (style == MarkdownStyle.None) return _font;
        if (!_markdownFonts.TryGetValue(style, out var font))
        {
            font = new(_typeface, style.HasFlag(MarkdownStyle.Heading1) ? 19 : style.HasFlag(MarkdownStyle.Heading2) ? 17 : 15)
            { Edging = SKFontEdging.SubpixelAntialias, Embolden = style.HasFlag(MarkdownStyle.Bold), SkewX = style.HasFlag(MarkdownStyle.Italic) ? -.18f : 0 };
            _markdownFonts.Add(style, font);
        }
        return font;
    }

    private List<ChatVisualLine> LayoutMarkdown(string source, float width)
    {
        var blocks = ChatMarkdown.Parse(source); List<ChatVisualLine> result = [];
        MarkdownBlock? previous = null;
        foreach (MarkdownBlock block in blocks)
        {
            if (previous is not null && !(previous.Prefix.Length > 0 && block.Prefix.Length > 0)
                && !(previous.Kind == MarkdownBlockKind.TableRow && block.Kind == MarkdownBlockKind.TableRow))
                result.Add(EmptyMarkdownLine(block.Start));
            float indent = Math.Min(block.Indent * CW, Math.Max(0, width - 6 * CW));
            if (block.Kind == MarkdownBlockKind.Rule)
                result.Add(EmptyMarkdownLine(block.Start) with { Rule = true });
            else if (block.Kind == MarkdownBlockKind.TableRow && block.Cells is { Count: > 0 } cells)
            {
                float columnWidth = (width - indent) / cells.Count;
                if (columnWidth < 8 * CW)
                {
                    // Narrow windows stack cells instead of clipping an unreadably narrow grid.
                    for (int c = 0; c < cells.Count; c++)
                        result.AddRange(WrapRich(source, cells[c], Math.Max(CW, width - indent - 3 * CW), block.Start)
                            .Select((line, i) => line with { Indent = indent + 3 * CW, Prefix = i == 0 ? $"{c + 1}. " : "", Quote = block.Quote }));
                }
                else
                {
                    var columns = cells.Select(cell => WrapRich(source, cell, columnWidth - 2 * CW, block.Start)).ToArray();
                    int height = columns.Max(lines => lines.Count);
                    for (int r = 0; r < height; r++)
                    {
                        List<ChatGlyph> glyphs = []; float used = 0;
                        for (int c = 0; c < columns.Length; c++)
                        {
                            float target = c * columnWidth + CW;
                            int sourceOffset = glyphs.Count == 0 ? block.Start : glyphs[^1].End;
                            if (r < columns[c].Count)
                            {
                                var cellGlyphs = columns[c][r].Styled!;
                                if (cellGlyphs.Count > 0) sourceOffset = cellGlyphs[0].Start;
                                glyphs.Add(new("", sourceOffset, sourceOffset, Math.Max(0, target - used), false, false));
                                glyphs.AddRange(cellGlyphs); used = target + cellGlyphs.Sum(g => g.Width);
                            }
                        }
                        result.Add(RichLine(glyphs, block.Start) with { Indent = indent, TableHeader = block.Header,
                            Columns = Enumerable.Range(0, cells.Count + 1).Select(c => c * columnWidth).ToArray() });
                    }
                }
            }
            else
            {
                bool code = block.Kind == MarkdownBlockKind.Code;
                float prefixWidth = block.Prefix.Length > 0 ? _font.MeasureText(block.Prefix) : 0;
                float bodyIndent = indent + prefixWidth + (code ? CW : 0);
                if (code) result.Add(EmptyMarkdownLine(block.Start) with { CodeHeader = string.IsNullOrWhiteSpace(block.Language) ? "Code" : block.Language });
                var lines = WrapRich(source, block.Runs, Math.Max(CW, width - bodyIndent - (code ? CW : 0)), block.Start);
                if (code && lines.Count > 1 && lines[^1].Text.Length == 0) lines.RemoveAt(lines.Count - 1);
                result.AddRange(lines.Select((line, index) => line with { Indent = bodyIndent, Prefix = index == 0 ? block.Prefix : "",
                    Quote = block.Quote, Code = code }));
            }
            previous = block;
        }
        if (result.Count == 0) result.Add(EmptyMarkdownLine(0));
        return result;
    }

    private static ChatVisualLine EmptyMarkdownLine(int offset) => new("", offset, offset, offset, [offset], [0], []);
    private ChatVisualLine RichLine(IReadOnlyList<ChatGlyph> glyphs, int emptyOffset, int? breakEnd = null)
    {
        if (glyphs.Count == 0) return EmptyMarkdownLine(emptyOffset) with { BreakEnd = breakEnd ?? emptyOffset };
        int[] offsets = new int[glyphs.Count + 1]; float[] advances = new float[offsets.Length];
        for (int i = 0; i < glyphs.Count; i++)
        { offsets[i] = glyphs[i].Start; advances[i + 1] = advances[i] + glyphs[i].Width; }
        offsets[^1] = glyphs[^1].End;
        return new(string.Concat(glyphs.Select(g => g.Text)), glyphs[0].Start, glyphs[^1].End, breakEnd ?? glyphs[^1].End,
            offsets, advances, glyphs);
    }

    private List<ChatVisualLine> WrapRich(string source, IReadOnlyList<MarkdownRun> runs, float width, int emptyOffset)
    {
        List<ChatGlyph> glyphs = [];
        foreach (MarkdownRun run in runs)
        {
            bool exact = run.End - run.Start == run.Text.Length && run.Start >= 0 && run.End <= source.Length
                && source.AsSpan(run.Start, run.Text.Length).SequenceEqual(run.Text);
            var elements = StringInfo.GetTextElementEnumerator(run.Text);
            while (elements.MoveNext())
            {
                string value = elements.GetTextElement(); bool newline = value is "\n" or "\r\n" or "\r";
                string visible = newline ? "" : TerminalText.Sanitize(value);
                int start = exact ? run.Start + elements.ElementIndex : run.Start;
                int end = exact ? start + value.Length : run.End;
                glyphs.Add(new(visible, start, end, MarkdownFont(run.Style).MeasureText(visible), newline,
                    !newline && string.IsNullOrWhiteSpace(value), run.Style, run.Url));
            }
        }
        List<ChatVisualLine> result = []; int first = 0;
        while (first < glyphs.Count)
        {
            int end = first, lastBreak = -1; float used = 0;
            while (end < glyphs.Count && !glyphs[end].Newline)
            {
                if (end > first && used + glyphs[end].Width > width) break;
                used += glyphs[end].Width; if (glyphs[end].Break) lastBreak = end + 1; end++;
            }
            if (end < glyphs.Count && !glyphs[end].Newline && lastBreak > first) end = lastBreak;
            bool newline = end < glyphs.Count && glyphs[end].Newline;
            result.Add(RichLine(glyphs.Skip(first).Take(end - first).ToArray(), glyphs[first].Start, newline ? glyphs[end].End : null));
            first = newline ? end + 1 : end;
        }
        if (glyphs.Count == 0 || glyphs[^1].Newline) result.Add(EmptyMarkdownLine(glyphs.Count == 0 ? emptyOffset : glyphs[^1].End));
        return result;
    }

    private void DrawMarkdownLine(ChatVisualLine line, Guid? id, ChatPreviewState chat, bool interactive, float x, float width, int row)
    {
        float y = row * CH;
        if (line.Code || line.TableHeader) Rect(x, y, width, CH, Raised);
        if (line.Quote > 0) Rect(x + Math.Max(0, line.Indent - 2 * CW), y, 2, CH, Accent.WithAlpha(130));
        if (line.Columns is { } columns)
        {
            foreach (float position in columns) LineSegment(x + line.Indent + position, y, x + line.Indent + position, y + CH, Border);
            LineSegment(x + line.Indent, y + CH - 1, x + width, y + CH - 1, Border);
        }
        if (line.Prefix.Length > 0) UiPixels(line.Prefix, x + line.Indent - _font.MeasureText(line.Prefix), y + 16, Accent, _font);
        var range = interactive && id is { } messageId && _ui.HistoryFocused ? chat.Selection.Range(chat.Messages, messageId) : null;
        _canvas.Save(); _canvas.ClipRect(SKRect.Create(x, y, width, CH));
        for (int i = 0; i < line.Styled!.Count; i++)
        {
            ChatGlyph glyph = line.Styled[i]; float gx = x + line.Indent + line.Advances[i];
            if (glyph.Style.HasFlag(MarkdownStyle.Code) && !line.Code) Rect(gx, y + 1, glyph.Width, CH - 2, Raised);
            if (range is { } selected && glyph.End > selected.Start && glyph.Start < selected.End)
                Rect(gx, y, glyph.Width, CH, Accent.WithAlpha(75));
            UiPixels(glyph.Text, gx, y + 16, glyph.Style.HasFlag(MarkdownStyle.Link) ? Accent : TextColor, MarkdownFont(glyph.Style));
            if (glyph.Style.HasFlag(MarkdownStyle.Link)) LineSegment(gx, y + 19, gx + glyph.Width, y + 19, Accent);
            if (glyph.Style.HasFlag(MarkdownStyle.Strike)) LineSegment(gx, y + 11, gx + glyph.Width, y + 11, TextColor);
        }
        if (range is { } selectedRange && line.BreakEnd > line.End && selectedRange.Start <= line.End && selectedRange.End > line.End)
            Rect(x + line.Indent + line.Advances[^1], y, CW, CH, Accent.WithAlpha(75));
        _canvas.Restore();
    }
}
