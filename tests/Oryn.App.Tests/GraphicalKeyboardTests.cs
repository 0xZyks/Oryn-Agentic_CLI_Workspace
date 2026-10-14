using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class GraphicalKeyboardTests
{
    [Fact]
    public void ReturningToChatAlwaysTargetsComposer()
    {
        using GraphicalWorkspaceController ui = Create();
        ui.Execute(KeyboardAction.FocusChat, 0); ui.Key(InputKey.Parse("Tab"), 0);
        Assert.True(ui.HistoryFocused);
        ui.Execute(KeyboardAction.FocusCode, 0); ui.Execute(KeyboardAction.FocusChat, 0);
        Assert.True(ui.AcceptsText); Assert.Equal("Ctrl+Tab", ui.Keyboard.DisplayHint(KeyboardAction.NextPanel));
        ui.Text("visible draft"); Assert.Equal("visible draft", ui.State.Chat.Draft);
        ui.Key(InputKey.Parse("Tab"), 0); ui.Key(InputKey.Parse("F6"), 0); ui.Key(InputKey.Parse("Enter"), 0);
        Assert.True(ui.AcceptsText); Assert.False(ui.HistoryFocused);
    }
    [Fact]
    public void HoldingFocusShortcutDoesNotToggleBackOrEnterGraphDetails()
    {
        using GraphicalWorkspaceController ui = Create(); ui.Execute(KeyboardAction.FocusChat, 0);
        ui.Key(InputKey.Parse("Ctrl+Enter"), 0);
        for (int i = 1; i < 10; i++) ui.Key(InputKey.Parse("Ctrl+Enter"), i, repeat: true);
        Assert.Equal(PanelFocusArea.Tabs, ui.State.FocusArea);
        ui.Execute(KeyboardAction.FocusVisualizer, 10); ui.Key(InputKey.Parse("Enter"), 10);
        for (int i = 11; i < 20; i++) ui.Key(InputKey.Parse("Enter"), i, repeat: true);
        Assert.NotNull(ui.State.Graph.SelectedMemberId); Assert.Equal(WorkspaceOverlay.None, ui.Overlay);
    }
    [Fact]
    public void RepeatsPreserveNavigationAndTextDeletion()
    {
        using GraphicalWorkspaceController ui = Create(); ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("abcd");
        ui.Key(InputKey.Parse("Backspace"), 0); ui.Key(InputKey.Parse("Backspace"), 1, repeat: true);
        Assert.Equal("ab", ui.Editor.Text);
        ui.State.LoadRoot(new("/tmp", Enumerable.Range(0, 5).Select(i => new FileEntry("/tmp/" + i, i.ToString(), false, false)).ToArray(), false, null));
        ui.Execute(KeyboardAction.FocusFiles, 2); ui.Key(InputKey.Parse("Down"), 2); ui.Key(InputKey.Parse("Down"), 3, repeat: true);
        Assert.Equal(2, ui.State.SelectedIndex);
    }
    [Fact]
    public void UnknownModifiedTabDoesNotActAsLocalTab()
    {
        using GraphicalWorkspaceController ui = Create(new() { Super = "F12" });
        ui.Execute(KeyboardAction.FocusChat, 0);
        ui.Key(InputKey.Parse("Ctrl+Tab"), 0); ui.Key(InputKey.Parse("Ctrl+Alt+Tab"), 0);
        Assert.True(ui.AcceptsText); Assert.False(ui.HistoryFocused);
        ui.Key(InputKey.Parse("Tab"), 0); Assert.True(ui.HistoryFocused);
    }
    [Fact]
    public void LeaderCommandsTakePriorityOverTextEditing()
    {
        using GraphicalWorkspaceController ui = Create(new() { Super = "F12" }); ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("draft");
        ui.Key(InputKey.Parse("F12"), 0); ui.Key(InputKey.Parse("Enter"), 1);
        Assert.Equal(PanelFocusArea.Tabs, ui.State.FocusArea); Assert.Empty(ui.State.Chat.Messages);
        Assert.Equal("F12 then Tab", ui.Keyboard.DisplayHint(KeyboardAction.NextPanel));
        ui.Key(InputKey.Parse("F12"), 2); ui.Key(InputKey.Parse("Enter"), 3);
        Assert.True(ui.AcceptsText); Assert.Equal("draft", ui.State.Chat.Draft);
    }
    [Fact]
    public void HoldingLeaderDoesNotExtendItsTimeout()
    {
        using GraphicalWorkspaceController ui = Create(new() { Super = "F12" });
        ui.Key(InputKey.Parse("F12"), 0); ui.Key(InputKey.Parse("F12"), 950, repeat: true); ui.Tick(1001);
        Assert.False(ui.Keyboard.LeaderPending);
    }
    [Fact]
    public void PlainCharacterBindingsDoNotStealComposerTyping()
    {
        KeyBindingSettings keys = new(); keys.Content["ContentDown"] = ["J", "Down"];
        using GraphicalWorkspaceController ui = Create(keys); ui.Execute(KeyboardAction.FocusChat, 0);
        Assert.False(ui.Key(InputKey.Parse("J"), 0).Handled); ui.Text("j");
        Assert.Equal("j", ui.State.Chat.Draft); Assert.Equal(0, ui.ChatScroll);
        ui.Key(InputKey.Parse("Left"), 1); ui.Text("a"); Assert.Equal("aj", ui.State.Chat.Draft);
    }
    [Fact]
    public void ModalConfirmAndCancelRequireUnmodifiedKeys()
    {
        using GraphicalWorkspaceController ui = Create(); ui.Execute(KeyboardAction.FocusVisualizer, 0);
        ui.OpenOverlay(WorkspaceOverlay.Options); ui.Key(InputKey.Parse("Alt+Enter"), 0);
        Assert.Equal(WorkspaceOverlay.Options, ui.Overlay);
        ui.Key(InputKey.Parse("Shift+Tab"), 1); Assert.Equal(2, ui.OverlaySelection);
        ui.Key(InputKey.Parse("Ctrl+Esc"), 2); Assert.Equal(WorkspaceOverlay.Options, ui.Overlay);
        ui.Key(InputKey.Parse("Esc"), 3); Assert.Equal(WorkspaceOverlay.None, ui.Overlay);
    }
    [Fact]
    public void ModalFocusCannotBeChangedByWorkspaceShortcuts()
    {
        using GraphicalWorkspaceController ui = Create(); ui.Execute(KeyboardAction.FocusVisualizer, 0);
        ui.OpenOverlay(WorkspaceOverlay.ElementChat); ui.Text("member draft");
        ui.Key(InputKey.Parse("Ctrl+Tab"), 1); ui.Key(InputKey.Parse("F2"), 2);
        Assert.Equal(WorkspaceOverlay.ElementChat, ui.Overlay); Assert.Equal("member draft", ui.CurrentChat.Draft);
        Assert.Empty(ui.State.Chat.Messages);
    }
    [Fact]
    public void ClipboardOwnsCtrlCInTextFieldsWithoutQuitting()
    {
        using GraphicalWorkspaceController ui = Create(); ui.Execute(KeyboardAction.FocusChat, 0);
        Assert.Equal(ClipboardRequest.Copy, ui.Key(InputKey.Parse("Ctrl+C"), 0).Clipboard);
        Assert.False(ui.State.QuitRequested);
        ui.OpenBindings(); Assert.Equal(ClipboardRequest.Paste, ui.Key(InputKey.Parse("Ctrl+V"), 1).Clipboard);
    }
    [Fact]
    public void WheelOnDockDoesNotStealKeyboardFocusFromComposer()
    {
        using GraphicalWorkspaceController ui = Create(); ui.Resize(1280, 836);
        ui.Execute(KeyboardAction.DockRight, 0); ui.Execute(KeyboardAction.FocusChat, 0);
        PanelBounds graph = ui.Layout.Visualizer; ui.Wheel(graph.X, graph.Y, 0, 1);
        Assert.Equal(WorkspacePanel.Center, ui.State.Focus); Assert.True(ui.AcceptsText);
        ui.Text("still typing"); Assert.Equal("still typing", ui.State.Chat.Draft);
    }
    [Fact]
    public async Task LateFileReadDoesNotSwitchAwayFromExplicitChatFocus()
    {
        DelayedFiles files = new(); WorkspaceState state = new("/tmp");
        state.LoadRoot(new("/tmp", [new("/tmp/example", "example", false, false)], false, null));
        using GraphicalWorkspaceController ui = new(state, files, new(new()), "/tmp/unused-oryn-key-profile");
        ui.OpenSelection(); ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("draft");
        files.Result.SetResult(new("/tmp/example", "old preview", false, null));
        await Task.Delay(30); ui.Tick(0);
        Assert.Equal(WorkspaceTab.Chat, state.ActiveTab); Assert.True(ui.AcceptsText); Assert.False(ui.Loading);
        Assert.Equal("draft", state.Chat.Draft);
    }
    [Fact]
    public void CtrlCIsNotADefaultQuitOutsideTheComposer()
    {
        using GraphicalWorkspaceController ui = Create(); ui.Key(InputKey.Parse("Ctrl+C"), 0);
        Assert.False(ui.State.QuitRequested);
        ui.Execute(KeyboardAction.FocusChat, 1); ui.Key(InputKey.Parse("F6"), 1); ui.Key(InputKey.Parse("Ctrl+C"), 2);
        Assert.False(ui.State.QuitRequested);
    }
    [Fact]
    public void FormerQuitDefaultIsMigratedWithoutChangingTheSavedFile()
    {
        string root = Path.Combine(Path.GetTempPath(), "oryn-quit-profile-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        string path = Path.Combine(root, "keys.json");
        try
        {
            string original = """{"Workspace":{"Quit":["Super+Q","Ctrl+C"]}}"""; File.WriteAllText(path, original);
            Assert.Equal(new[] { "Super+Q" }, KeyBindingStore.Load(path).Workspace["Quit"]); Assert.Equal(original, File.ReadAllText(path));
            File.WriteAllText(path, """{"Workspace":{"Quit":["Ctrl+C"]}}""");
            Assert.Throws<ArgumentException>(() => KeyBindingStore.Load(path)); // Ctrl+C is owned by text fields.
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void AmbiguousPanelTabRemapIsRejectedInsteadOfStealingLocalTraversal()
    {
        KeyBindingSettings keys = new(); keys.Panels["NextPanel"] = ["Tab"];
        Assert.Throws<ArgumentException>(() => new KeyboardRouter(keys));
    }
    private static GraphicalWorkspaceController Create(KeyBindingSettings? keys = null) =>
        new(new("/tmp"), new LocalWorkspaceFiles("/tmp", new()), new(keys ?? new()), "/tmp/unused-oryn-key-profile");
    private sealed class DelayedFiles : IWorkspaceFiles
    {
        public TaskCompletionSource<FilePreview> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<DirectoryResult> ListAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public Task<FilePreview> ReadAsync(string path, CancellationToken token) => Result.Task;
    }
}
