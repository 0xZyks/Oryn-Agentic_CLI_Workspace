using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class GraphicalWorkspaceTests
{
    [Fact]
    public void EditingNeverSplitsEmojiOrCombiningCharacters()
    {
        TextEditState editor = new(); editor.Insert("a👩‍💻e\u0301");
        editor.Backspace(); Assert.Equal("a👩‍💻", editor.Text);
        editor.Move("LEFT", false); Assert.Equal(1, editor.Cursor);
        editor.Delete(); Assert.Equal("a", editor.Text);
        editor.SelectAll(); editor.Insert("hello\r\nworld"); Assert.Equal("hello\nworld", editor.Text);
        editor.Move("HOME", false); editor.Move("UP", false); Assert.Equal(0, editor.Cursor);
    }
    [Fact]
    public void DraftLimitAndSelectionArePreserved()
    {
        TextEditState editor = new(); editor.Insert(new string('a', TextEditState.MaximumLength));
        editor.Insert("overflow"); Assert.Equal(TextEditState.MaximumLength, editor.Text.Length);
        editor.SelectAll(); editor.Insert("short"); Assert.Equal("short", editor.Text);
        editor.Move("LEFT", true); Assert.Equal("t", editor.Selection);
        editor.Backspace(); Assert.Equal("shor", editor.Text);
    }
    [Fact]
    public void TextEntryNavigationAndReplyDoNotStealNextDraft()
    {
        using GraphicalWorkspaceController ui = Create();
        ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("First");
        ui.Key(new("ENTER", InputModifiers.Shift), 0); ui.Text("line");
        Assert.Equal("First\nline", ui.State.Chat.Draft);
        ui.Key(new("ENTER"), 0); ui.Text("Next");
        ui.Tick(1600); Assert.Equal("Next", ui.Editor.Text); Assert.Equal("Next", ui.State.Chat.Draft);
        Assert.Equal(2, ui.State.Chat.Messages.Count);
        ui.Key(new("TAB"), 1700); Assert.True(ui.HistoryFocused); Assert.Equal(WorkspacePanel.Center, ui.State.Focus);
        ui.Key(new("TAB", InputModifiers.Ctrl), 1700); Assert.Equal(WorkspacePanel.Explorer, ui.State.Focus);
    }
    [Fact]
    public void ElementChatIsIsolatedAndClosingOverlayRestoresGlobalDraft()
    {
        using GraphicalWorkspaceController ui = Create(); ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("global draft");
        ui.Execute(KeyboardAction.FocusVisualizer, 0); ui.State.Graph.EnterComponent();
        Guid member = ui.State.Graph.Target.Id;
        ui.OpenOverlay(WorkspaceOverlay.ElementChat); ui.Text("member"); ui.Key(new("ENTER"), 0); ui.Tick(1600); ui.Text("unsent"); ui.CloseOverlay();
        Assert.Equal("global draft", ui.Editor.Text); Assert.Empty(ui.State.Chat.Messages);
        ui.OpenOverlay(WorkspaceOverlay.ElementChat); Assert.Equal("unsent", ui.Editor.Text);
        Assert.Equal(2, ui.State.Graph.ChatFor(member).Messages.Count);
        ui.CloseOverlay(); ui.State.Graph.Move(0, 1); ui.OpenOverlay(WorkspaceOverlay.ElementChat);
        Assert.Empty(ui.CurrentChat.Messages); Assert.Equal("", ui.Editor.Text);
    }
    [Fact]
    public void ModalKeysDoNotMutateUnderlyingGraphAndFocusLossCancelsResize()
    {
        using GraphicalWorkspaceController ui = Create(); ui.Resize(1280, 836);
        ui.Execute(KeyboardAction.FocusVisualizer, 0); Guid selected = ui.State.Graph.SelectedId;
        ui.OpenOverlay(WorkspaceOverlay.Options); ui.Key(new("RIGHT"), 0); Assert.Equal(selected, ui.State.Graph.SelectedId);
        ui.CloseOverlay();
        PanelBounds split = ui.Layout.ExplorerSplitter;
        Assert.True(ui.State.BeginResize(split.X, split.Y, ui.Columns, ui.Rows));
        WorkspaceLayoutOptions original = ui.State.LayoutOptions;
        ui.PointerMove(split.X + 3); ui.FocusLost();
        Assert.Null(ui.State.ResizingSplitter); Assert.Equal(original, ui.State.LayoutOptions);
        ui.Resize(540, 352); Assert.True(ui.Layout.Compact);
    }
    [Fact]
    public async Task SupersededReadCannotReplaceNewerPreview()
    {
        DelayedFiles files = new(); WorkspaceState state = new("/tmp");
        state.LoadRoot(new("/tmp", [new("/tmp/a", "a", false, false), new("/tmp/b", "b", false, false)], false, null));
        using GraphicalWorkspaceController ui = new(state, files, new(new()), "/tmp/unused-oryn-test-profile");
        ui.OpenSelection(); state.Select(1); ui.OpenSelection();
        files.Second.SetResult(new("/tmp/b", "new", false, null));
        await WaitPreview(ui, "/tmp/b"); files.First.SetResult(new("/tmp/a", "old", false, null));
        await Task.Delay(20); ui.Tick(0); Assert.Equal("/tmp/b", state.Preview!.Path); Assert.False(ui.Loading);
    }
    [Fact]
    public void InvalidBindingDoesNotReplaceRouterOrWriteProfile()
    {
        using GraphicalWorkspaceController ui = Create(); KeyboardRouter original = ui.Keyboard;
        ui.OpenBindings(); ui.BindingEditor.Set("invalid modifier");
        Assert.False(ui.ApplyBindings(true)); Assert.Same(original, ui.Keyboard); Assert.NotEmpty(ui.BindingError);
    }
    [Fact]
    public void ImeConfirmationDoesNotSendUncommittedDraft()
    {
        using GraphicalWorkspaceController ui = Create(); ui.Execute(KeyboardAction.FocusChat, 0);
        ui.Text("draft"); ui.Composition = "composition"; ui.Key(new("ENTER"), 0);
        Assert.Empty(ui.State.Chat.Messages); Assert.Equal("draft", ui.State.Chat.Draft);
        ui.Text(" committed"); ui.Key(new("ENTER"), 0);
        Assert.Equal("draft committed", ui.State.Chat.Messages[0].Text);
    }
    [Fact]
    public void WheelPositionSurvivesPaintingAtUnchangedDimensions()
    {
        using GraphicalWorkspaceController ui = Create();
        ui.State.LoadRoot(new("/tmp", Enumerable.Range(0, 80).Select(i => new FileEntry("/tmp/" + i, i.ToString(), false, false)).ToArray(), false, null));
        ui.Resize(1280, 836); PanelBounds files = ui.Layout.Explorer;
        ui.Wheel(files.X, files.Y, 0, -1); int top = ui.ExplorerTop;
        Assert.True(top > 0); ui.Resize(1280, 836); Assert.Equal(top, ui.ExplorerTop);
    }
    [Fact]
    public void WheelInChromeGapsDoesNotScrollUnderlyingExplorer()
    {
        using GraphicalWorkspaceController ui = Create();
        ui.State.LoadRoot(new("/tmp", Enumerable.Range(0, 80).Select(i => new FileEntry("/tmp/" + i, i.ToString(), false, false)).ToArray(), false, null));
        ui.Resize(1280, 836); PanelBounds files = ui.Layout.Explorer;
        float x = (files.X + 5) * 9, top = files.Y * 22 + ui.Spacing.PanelTopOffset;
        ui.WheelPixels(x, top - 2, 0, -1); Assert.Equal(0, ui.ExplorerTop);
        ui.WheelPixels(x, (files.Y + files.Height) * 22 - 2, 0, -1); Assert.Equal(0, ui.ExplorerTop);
        ui.WheelPixels(x, top + 100, 0, -1); Assert.True(ui.ExplorerTop > 0);
    }
    private static GraphicalWorkspaceController Create() => new(new("/tmp"), new LocalWorkspaceFiles("/tmp", new()), new(new()), "/tmp/unused-oryn-test-profile");
    private static async Task WaitPreview(GraphicalWorkspaceController ui, string path)
    {
        for (int i = 0; i < 100; i++) { ui.Tick(0); if (ui.State.Preview?.Path == path) return; await Task.Delay(5); }
        throw new TimeoutException("Preview did not arrive.");
    }
    private sealed class DelayedFiles : IWorkspaceFiles
    {
        public TaskCompletionSource<FilePreview> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<FilePreview> Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DirectoryResult> ListAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public Task<FilePreview> ReadAsync(string path, CancellationToken token) => path.EndsWith('a') ? First.Task : Second.Task;
    }
}
