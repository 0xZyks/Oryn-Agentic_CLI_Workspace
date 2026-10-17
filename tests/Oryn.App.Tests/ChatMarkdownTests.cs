using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class ChatMarkdownTests
{
    [Fact]
    public void CommonFormattingRetainsSourceSpansAndReadableText()
    {
        const string source = "# Titre\n\nDu **gras**, de l'*italique*, du ~~barré~~ et `x = 1`. [Docs](https://example.invalid).";
        var blocks = ChatMarkdown.Parse(source); var runs = blocks.SelectMany(b => b.Runs).ToArray();
        Assert.Equal(MarkdownBlockKind.Heading, blocks[0].Kind);
        Assert.Contains(runs, r => r.Text == "gras" && r.Style.HasFlag(MarkdownStyle.Bold));
        Assert.Contains(runs, r => r.Text == "italique" && r.Style.HasFlag(MarkdownStyle.Italic));
        Assert.Contains(runs, r => r.Text == "barré" && r.Style.HasFlag(MarkdownStyle.Strike));
        Assert.Contains(runs, r => r.Text == "x = 1" && r.Style.HasFlag(MarkdownStyle.Code));
        Assert.Contains(runs, r => r.Text == "Docs" && r.Url == "https://example.invalid");
        Assert.All(runs, r => { Assert.InRange(r.Start, 0, source.Length); Assert.InRange(r.End, r.Start, source.Length); });
        Assert.Equal("Titre\n\nDu gras, de l'italique, du barré et x = 1. Docs.", ChatMarkdown.CopySelection(source, 0, source.Length));
    }
    [Fact]
    public void CodeFencesPreserveLiteralContentAndStillWorkBeforeTheClosingFenceArrives()
    {
        const string prefix = "```csharp\n\tvar text = \"**literal**\";\n";
        foreach (string source in new[] { prefix, prefix + "```" })
        {
            var code = Assert.Single(ChatMarkdown.Parse(source)); Assert.Equal(MarkdownBlockKind.Code, code.Kind);
            Assert.Equal("csharp", code.Language); Assert.All(code.Runs, r => Assert.True(r.Style.HasFlag(MarkdownStyle.Code)));
            Assert.Equal("\tvar text = \"**literal**\";\n", ChatMarkdown.CopySelection(source, 0, source.Length));
        }
    }
    [Fact]
    public void TablesListsAndQuotesRemainStructuredWithoutExecutingHtmlOrFetchingImages()
    {
        const string source = "- item\n  - enfant\n\n> citation\n\n| Nom | État |\n| --- | --- |\n| A | **OK** |\n\n![alt](https://example.invalid/image.png)\n\n<script>alert(1)</script>";
        var blocks = ChatMarkdown.Parse(source);
        Assert.Contains(blocks, b => b.Prefix == "• " && b.Indent > 0);
        Assert.Contains(blocks, b => b.Quote > 0);
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.TableRow && b.Header && b.Cells!.Count == 2);
        Assert.Contains(blocks, b => b.Kind == MarkdownBlockKind.TableRow && !b.Header);
        string text = ChatMarkdown.CopySelection(source, 0, source.Length);
        Assert.Contains("alt", text); Assert.DoesNotContain("image.png", text); Assert.Contains("<script>alert(1)</script>", text);
    }
    [Fact]
    public void EscapesEntitiesAndUnicodeAreCopiedAsDisplayed()
    {
        const string source = "A &amp; B \\*littéral\\* 👩‍💻 e\u0301";
        Assert.Equal("A & B *littéral* 👩‍💻 e\u0301", ChatMarkdown.CopySelection(source, 0, source.Length));
    }
    [Fact]
    public void CopyingAnExcerptOmitsHiddenMarkupWhileTheMessageButtonKeepsTheSource()
    {
        const string source = "Avant **important** et `code` après.";
        using GraphicalWorkspaceController ui = new(new("/tmp"), new LocalWorkspaceFiles("/tmp", new()), new(new()), "/tmp/unused-md-keys.json");
        ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("draft");
        var message = new ChatPreviewMessage(ChatPreviewRole.Oryn, source); ui.State.Chat.Load([message]);
        ui.BeginChatSelection(message.Id, source.IndexOf("important", StringComparison.Ordinal));
        ui.ExtendChatSelection(message.Id, source.IndexOf(" après", StringComparison.Ordinal)); ui.PointerUp();
        ui.Key(InputKey.Parse("Ctrl+C"), 0); Assert.Equal("important et code", ui.TakeClipboardText());
        ui.CopyChatMessage(message.Id); Assert.Equal(source, ui.TakeClipboardText()); Assert.Equal("draft", ui.State.Chat.Draft);
        Assert.Equal(source, ui.State.Chat.Messages[0].Text);
    }
    [Theory]
    [InlineData("**incomplet")]
    [InlineData("[lien](https://")]
    [InlineData("| a | b |\n| --")]
    [InlineData("***")]
    [InlineData("```")]
    [InlineData("```json")]
    [InlineData("```json\n")]
    [InlineData("```json\n```")]
    public void PartialStreamingMarkdownRemainsParseable(string source)
    {
        var blocks = ChatMarkdown.Parse(source);
        Assert.All(blocks.SelectMany(b => b.Runs), r => { Assert.InRange(r.Start, 0, source.Length); Assert.InRange(r.End, r.Start, source.Length); });
        _ = ChatMarkdown.CopySelection(source, 0, source.Length);
    }
}
