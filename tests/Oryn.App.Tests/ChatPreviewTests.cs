using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class ChatPreviewTests
{
    [Fact]
    public void EmptyPromptKeepsDraftAndCreatesNoReply()
    {
        ChatPreviewState chat = new() { Draft = "  " };
        Assert.False(chat.Send());
        Assert.Empty(chat.Messages);
        Assert.Equal("  ", chat.Draft);
    }

    [Fact]
    public void SendingAppendsOrderedPairsAndClearsDraftWithoutLosingHistory()
    {
        ChatPreviewState chat = new() { Draft = " Say Hello World! " };
        Assert.True(chat.Send());
        Assert.Equal("", chat.Draft);
        Assert.True(chat.IsThinking);
        Assert.Single(chat.Messages);
        Assert.True(chat.CompleteReply());
        chat.Draft = "Bonjour 世界";
        Assert.True(chat.Send());
        Assert.True(chat.CompleteReply());
        Assert.Equal(new[] {
            new ChatPreviewMessage(ChatPreviewRole.User, "Say Hello World!"),
            new ChatPreviewMessage(ChatPreviewRole.Oryn, "Hello World!"),
            new ChatPreviewMessage(ChatPreviewRole.User, "Bonjour 世界"),
            new ChatPreviewMessage(ChatPreviewRole.Oryn, "Hello World!") }, chat.Messages);
    }

    [Fact]
    public void SwitchingTabsPreservesConversationAndDraftInTheWorkspaceSession()
    {
        WorkspaceState workspace = new("/tmp");
        workspace.Chat.Draft = "First";
        workspace.Chat.Send();
        workspace.Chat.CompleteReply();
        workspace.Chat.Draft = "Unsent";
        workspace.Dispatch(WorkspaceCommand.CodeTab);
        workspace.Dispatch(WorkspaceCommand.ChatTab);
        Assert.Equal("Unsent", workspace.Chat.Draft);
        Assert.Equal(2, workspace.Chat.Messages.Count);
    }

    [Fact]
    public void PendingReplyCannotBeSubmittedTwiceAndKeepsTheNextDraft()
    {
        ChatPreviewState chat = new() { Draft = "First" };
        Assert.True(chat.Send());
        chat.Draft = "Next";
        Assert.False(chat.Send());
        Assert.Equal("Next", chat.Draft);
        Assert.Single(chat.Messages);
        Assert.True(chat.CompleteReply());
        Assert.False(chat.CompleteReply());
        Assert.False(chat.IsThinking);
        Assert.Equal(2, chat.Messages.Count);
        Assert.True(chat.Send());
    }
}
