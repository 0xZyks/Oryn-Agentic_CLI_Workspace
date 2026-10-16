using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class ChatSelectionTests
{
    [Fact]
    public void SelectionNeverSplitsEmojiOrCombiningCharactersAndSupportsReverseDragging()
    {
        ChatPreviewState chat = new(); var message = new ChatPreviewMessage(ChatPreviewRole.Oryn, "A👩‍💻e\u0301Z");
        chat.Load([message]); chat.Selection.Begin(chat.Messages, message.Id, 2); chat.Selection.Extend(chat.Messages, message.Id, 7);
        Assert.Equal("👩‍💻", chat.Selection.Text(chat.Messages));
        chat.Selection.Begin(chat.Messages, message.Id, 8); chat.Selection.Extend(chat.Messages, message.Id, 1);
        Assert.Equal("👩‍💻e\u0301", chat.Selection.Text(chat.Messages));
    }
    [Fact]
    public void CrossMessageSelectionKeepsOriginalWhitespaceAndIdsWhenHistoryIsPrepended()
    {
        ChatPreviewState chat = new();
        var user = new ChatPreviewMessage(ChatPreviewRole.User, "before\tselected\r\nline");
        var reply = new ChatPreviewMessage(ChatPreviewRole.Oryn, "answer\nend");
        chat.Load([user, reply]); chat.Selection.Begin(chat.Messages, reply.Id, 6); chat.Selection.Extend(chat.Messages, user.Id, 7);
        string expected = "selected\r\nline\n\nanswer";
        Assert.Equal(expected, chat.Selection.Text(chat.Messages));
        chat.Prepend([new(ChatPreviewRole.User, "older")]);
        Assert.Equal(expected, chat.Selection.Text(chat.Messages));
    }
    [Fact]
    public void StreamingAppendsDoNotExpandAnExistingSelectionOrChangeItsMessageIdentity()
    {
        ChatPreviewState chat = new() { Draft = "question" }; chat.Send(); chat.BeginReply(); chat.AppendReply("first");
        Guid id = chat.Messages[^1].Id;
        chat.Selection.Begin(chat.Messages, id, 0); chat.Selection.Extend(chat.Messages, id, 5); chat.Selection.End();
        chat.AppendReply(" second");
        Assert.Equal("first", chat.Selection.Text(chat.Messages)); Assert.Equal(id, chat.Messages[^1].Id);
    }
    [Fact]
    public void HistoryCopyAndSelectAllAreReadOnlyAndReturningToComposerRestoresEditing()
    {
        using var ui = Create(); ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("unsent draft");
        var message = new ChatPreviewMessage(ChatPreviewRole.Oryn, "one two three"); ui.State.Chat.Load([message]);
        ui.BeginChatSelection(message.Id, 4); ui.ExtendChatSelection(message.Id, 7); ui.PointerUp();
        Assert.Equal(KeyboardScope.History, ui.InputScope); Assert.False(ui.AcceptsText);
        Assert.True(ui.Key(InputKey.Parse("Ctrl+C"), 0).Handled); Assert.Equal("two", ui.TakeClipboardText());
        ui.Key(InputKey.Parse("Backspace"), 0); ui.Key(InputKey.Parse("Ctrl+X"), 0); ui.Text("discard");
        Assert.Equal("unsent draft", ui.State.Chat.Draft); Assert.Equal("one two three", ui.State.Chat.Messages[^1].Text);
        ui.Key(InputKey.Parse("Ctrl+A"), 0); ui.Key(InputKey.Parse("Ctrl+C"), 0);
        Assert.Equal(message.Text, ui.TakeClipboardText());
        ui.Key(InputKey.Parse("Tab"), 0); Assert.True(ui.AcceptsText); ui.Text("!");
        Assert.Equal("unsent draft!", ui.State.Chat.Draft);
    }
    [Fact]
    public void ElementHistoryCopiesOnlyItsOwnChatAndFocusLossStopsDragging()
    {
        using var ui = Create(); ui.OpenOverlay(WorkspaceOverlay.ElementChat);
        var message = new ChatPreviewMessage(ChatPreviewRole.Oryn, "element answer"); ui.CurrentChat.Load([message]);
        ui.BeginChatSelection(message.Id, 0); ui.ExtendChatSelection(message.Id, 7); ui.FocusLost();
        Assert.False(ui.CurrentChat.Selection.Dragging); Assert.Equal(KeyboardScope.ElementHistory, ui.InputScope);
        ui.Key(InputKey.Parse("Ctrl+C"), 0); Assert.Equal("element", ui.TakeClipboardText());
        ui.Key(InputKey.Parse("Esc"), 0); Assert.Empty(ui.CurrentChat.Selection.Text(ui.CurrentChat.Messages));
        Assert.Equal(WorkspaceOverlay.ElementChat, ui.Overlay); Assert.Empty(ui.State.Chat.Messages);
        ui.Key(InputKey.Parse("Tab"), 0); Assert.True(ui.AcceptsText);
    }
    private static GraphicalWorkspaceController Create() => new(new("/tmp"), new LocalWorkspaceFiles("/tmp", new()), new(new()), "/tmp/unused-selection-keys.json");
}
