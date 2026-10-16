using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class ChatPreviewTests
{
    [Fact]
    public void EmptyPromptKeepsDraftAndCreatesNoReply()
    {
        ChatPreviewState chat = new() { Draft = "  " };
        Assert.False(chat.Send()); Assert.Empty(chat.Messages); Assert.Equal("  ", chat.Draft);
    }
    [Fact]
    public void StreamingPreservesIdentitySequenceUnicodeAndTheNextDraft()
    {
        ChatPreviewState chat = new() { Draft = " Bonjour " };
        Assert.True(chat.Send()); chat.BeginReply();
        Guid identity = chat.Messages[^1].Id;
        chat.Draft = "prochaine question";
        chat.AppendReply("Salut "); chat.AppendReply("世界");
        chat.FinishReply("Salut 世界", ChatMessageStatus.Complete);
        Assert.Equal(identity, chat.Messages[^1].Id);
        Assert.Equal("Salut 世界", chat.Messages[^1].Text);
        Assert.Equal("prochaine question", chat.Draft);
        Assert.Equal(new long[] { 1, 2 }, chat.Messages.Select(m => m.Sequence));
        Assert.Equal(2, chat.Messages.Select(m => m.Id).Distinct().Count()); Assert.False(chat.IsThinking);
    }
    [Fact]
    public void SwitchingTabsPreservesConversationAndDraftInTheWorkspaceSession()
    {
        WorkspaceState workspace = new("/tmp");
        workspace.Chat.Draft = "First"; workspace.Chat.Send(); workspace.Chat.BeginReply();
        workspace.Chat.FinishReply("Answer", ChatMessageStatus.Complete); workspace.Chat.Draft = "Unsent";
        workspace.Dispatch(WorkspaceCommand.CodeTab); workspace.Dispatch(WorkspaceCommand.ChatTab);
        Assert.Equal("Unsent", workspace.Chat.Draft); Assert.Equal(2, workspace.Chat.Messages.Count);
    }
    [Fact]
    public void CancelledReplyKeepsPartialContentAndRejectsLateChunks()
    {
        ChatPreviewState chat = new() { Draft = "First" };
        Assert.True(chat.Send()); chat.BeginReply(); chat.AppendReply("partial");
        chat.Draft = "Next"; Assert.False(chat.Send());
        chat.FinishReply("partial", ChatMessageStatus.Cancelled); chat.AppendReply("late");
        Assert.Equal("partial", chat.Messages[^1].Text); Assert.Equal("Next", chat.Draft);
        Assert.True(chat.Send());
    }
}
