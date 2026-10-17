using System.Text;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Oryn.App;

[Flags]
public enum MarkdownStyle { None = 0, Bold = 1, Italic = 2, Code = 4, Strike = 8, Link = 16, Heading1 = 32, Heading2 = 64 }
public enum MarkdownBlockKind { Paragraph, Heading, Code, Rule, TableRow }
public sealed record MarkdownRun(string Text, int Start, int End, MarkdownStyle Style = MarkdownStyle.None, string? Url = null);
public sealed record MarkdownBlock(MarkdownBlockKind Kind, IReadOnlyList<MarkdownRun> Runs, int Start, int End,
    int Indent = 0, string Prefix = "", int Quote = 0, string Language = "", bool Header = false,
    IReadOnlyList<IReadOnlyList<MarkdownRun>>? Cells = null);

/// <summary>Native-renderable Markdown with source spans. No HTML execution, remote assets or tool calls.</summary>
public static class ChatMarkdown
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePreciseSourceLocation()
        .DisableHtml().UsePipeTables().UseTaskLists().UseEmphasisExtras(EmphasisExtraOptions.Strikethrough).Build();

    public static IReadOnlyList<MarkdownBlock> Parse(string source)
    {
        List<MarkdownBlock> result = [];
        if (source.Length > 262144)
            return [new(MarkdownBlockKind.Paragraph, [new(source, 0, source.Length)], 0, source.Length)];
        foreach (Block block in Markdown.Parse(source, Pipeline)) Walk(block, 0, "", 0, 0);
        return result;

        void Walk(Block block, int indent, string prefix, int quote, int depth)
        {
            if (depth > 48) { Raw(block, indent, prefix, quote); return; }
            if (block is Table table)
            {
                foreach (TableRow row in table)
                {
                    var cells = row.OfType<TableCell>().Select(cell => (IReadOnlyList<MarkdownRun>)cell.OfType<LeafBlock>()
                        .SelectMany(leaf => InlineRuns(leaf.Inline, row.IsHeader ? MarkdownStyle.Bold : MarkdownStyle.None)).ToArray()).ToArray();
                    result.Add(new(MarkdownBlockKind.TableRow, cells.SelectMany(c => c).ToArray(), row.Span.Start, row.Span.End + 1,
                        indent, prefix, quote, Header: row.IsHeader, Cells: cells));
                }
            }
            else if (block is ListBlock list)
            {
                int order = int.TryParse(list.OrderedStart, out int parsed) ? parsed : 1;
                foreach (Block item in list)
                {
                    string bullet = list.IsOrdered ? $"{order++}{list.OrderedDelimiter} " : "• ";
                    Walk(item, indent, bullet, quote, depth + 1);
                }
            }
            else if (block is QuoteBlock quoted)
                foreach (Block child in quoted) Walk(child, indent + 2, prefix, quote + 1, depth + 1);
            else if (block is CodeBlock code)
            {
                List<MarkdownRun> runs = [];
                for (int i = 0; i < code.Lines.Count; i++)
                {
                    var line = code.Lines.Lines[i];
                    string text = line.Slice.ToString();
                    int start = Math.Clamp(line.Position, 0, source.Length);
                    // Slices backed by the original source retain exact indentation/content positions.
                    if (ReferenceEquals(line.Slice.Text, source)) start = line.Slice.Start;
                    else if (text.Length > 0)
                    {
                        int stop = source.IndexOf('\n', start); if (stop < 0) stop = source.Length;
                        int match = source.IndexOf(text, start, stop - start, StringComparison.Ordinal);
                        if (match >= 0) start = match;
                    }
                    int end = Math.Min(source.Length, start + text.Length);
                    runs.Add(new(text, start, end, MarkdownStyle.Code));
                    if (end < source.Length && source[end] == '\r') end++;
                    if (end < source.Length && source[end] == '\n') runs.Add(new("\n", end, end + 1, MarkdownStyle.Code));
                }
                result.Add(new(MarkdownBlockKind.Code, runs, code.Span.Start, code.Span.End + 1, indent, prefix, quote,
                    code is FencedCodeBlock fenced ? fenced.Info ?? "" : ""));
            }
            else if (block is ThematicBreakBlock)
                result.Add(new(MarkdownBlockKind.Rule, [], block.Span.Start, block.Span.End + 1, indent, prefix, quote));
            else if (block is LeafBlock leaf)
            {
                MarkdownStyle style = block is HeadingBlock heading ? MarkdownStyle.Bold | (heading.Level == 1 ? MarkdownStyle.Heading1
                    : heading.Level == 2 ? MarkdownStyle.Heading2 : MarkdownStyle.None) : MarkdownStyle.None;
                var runs = InlineRuns(leaf.Inline, style);
                if (leaf.Inline is null) { Raw(block, indent, prefix, quote); return; }
                result.Add(new(block is HeadingBlock ? MarkdownBlockKind.Heading : MarkdownBlockKind.Paragraph, runs,
                    block.Span.Start, block.Span.End + 1, indent, prefix, quote));
            }
            else if (block is ContainerBlock container)
            {
                bool first = true;
                foreach (Block child in container)
                { Walk(child, indent + (container is ListItemBlock && child is ListBlock ? 2 : 0), first ? prefix : "", quote, depth + 1); first = false; }
            }
            else Raw(block, indent, prefix, quote);
        }
        void Raw(Block block, int indent, string prefix, int quote)
        {
            int start = Math.Clamp(block.Span.Start, 0, source.Length), end = Math.Clamp(block.Span.End + 1, start, source.Length);
            result.Add(new(MarkdownBlockKind.Paragraph, [new(source[start..end], start, end)], start, end, indent, prefix, quote));
        }
        List<MarkdownRun> InlineRuns(ContainerInline? container, MarkdownStyle style, string? url = null, int depth = 0)
        {
            List<MarkdownRun> runs = [];
            if (container is null) return runs;
            if (depth > 48)
            {
                int first = Math.Clamp(container.Span.Start, 0, source.Length), last = Math.Clamp(container.Span.End + 1, first, source.Length);
                return [new(source[first..last], first, last, style)];
            }
            foreach (Inline node in container)
            {
                int start = Math.Clamp(node.Span.Start, 0, source.Length), end = Math.Clamp(node.Span.End + 1, start, source.Length);
                switch (node)
                {
                    case LiteralInline literal:
                        if (literal.IsFirstCharacterEscaped && start < end && source[start] == '\\') start++;
                        runs.Add(new(literal.Content.ToString(), start, end, style, url)); break;
                    case CodeInline inlineCode:
                        start = Math.Min(end, start + inlineCode.DelimiterCount); end = Math.Max(start, end - inlineCode.DelimiterCount);
                        string content = inlineCode.Content;
                        int match = source.IndexOf(content, start, end - start, StringComparison.Ordinal);
                        if (match >= 0) { start = match; end = start + content.Length; }
                        runs.Add(new(content, start, end, style | MarkdownStyle.Code)); break;
                    case EmphasisInline emphasis:
                        var extra = emphasis.DelimiterChar == '~' ? MarkdownStyle.Strike : emphasis.DelimiterCount >= 2 ? MarkdownStyle.Bold : MarkdownStyle.Italic;
                        runs.AddRange(InlineRuns(emphasis, style | extra, url, depth + 1)); break;
                    case LinkInline link:
                        // Images are represented by their alt text; no image/network loader is involved.
                        runs.AddRange(InlineRuns(link, style | (link.IsImage ? MarkdownStyle.Italic : MarkdownStyle.Link), link.IsImage ? null : link.Url, depth + 1)); break;
                    case AutolinkInline auto:
                        runs.Add(new(auto.Url, start + 1, Math.Max(start + 1, end - 1), style | MarkdownStyle.Link, auto.Url)); break;
                    case LineBreakInline:
                        runs.Add(new("\n", start, end, style)); break;
                    case HtmlEntityInline entity:
                        runs.Add(new(entity.Transcoded.ToString(), start, end, style, url)); break;
                    case TaskList task:
                        runs.Add(new(task.Checked ? "[x]" : "[ ]", start, end, style)); break;
                    case ContainerInline nested:
                        runs.AddRange(InlineRuns(nested, style, url, depth + 1)); break;
                    default:
                        runs.Add(new(source[start..end], start, end, style, url)); break;
                }
            }
            return runs;
        }
    }

    public static string CopySelection(string source, int start, int end)
    {
        start = Math.Clamp(start, 0, source.Length); end = Math.Clamp(end, start, source.Length);
        if (start == end) return "";
        StringBuilder result = new();
        foreach (MarkdownBlock block in Parse(source))
        {
            StringBuilder part = new();
            if (block.Prefix.Length > 0 && start <= block.Start && end > block.Start) part.Append(' ', block.Indent).Append(block.Prefix);
            var groups = block.Cells ?? new[] { block.Runs };
            foreach (var cell in groups)
            {
                if (part.Length > 0 && block.Cells is not null) part.Append('\t');
                foreach (MarkdownRun run in cell)
                {
                    if (run.End <= start || run.Start >= end) continue;
                    if (run.End - run.Start == run.Text.Length && source.AsSpan(run.Start, run.End - run.Start).SequenceEqual(run.Text))
                        part.Append(run.Text.AsSpan(Math.Max(start, run.Start) - run.Start, Math.Min(end, run.End) - Math.Max(start, run.Start)));
                    else part.Append(run.Text);
                }
            }
            if (part.Length == 0) continue;
            if (result.Length > 0)
            {
                int wanted = block.Prefix.Length > 0 || block.Kind == MarkdownBlockKind.TableRow ? 1 : 2;
                int existing = 0; for (int i = result.Length - 1; i >= 0 && result[i] == '\n'; i--) existing++;
                if (existing < wanted) result.Append('\n', wanted - existing);
            }
            result.Append(part);
        }
        return result.ToString();
    }
}
