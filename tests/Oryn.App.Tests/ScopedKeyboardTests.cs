using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class ScopedKeyboardTests
{
    [Theory]
    [InlineData(WorkspacePanel.Explorer)]
    [InlineData(WorkspacePanel.Center)]
    [InlineData(WorkspacePanel.Visualizer)]
    public void CtrlEnterSelectsCentralTabsFromEveryPanel(WorkspacePanel panel)
    {
        using GraphicalWorkspaceController ui = Create();
        if (panel == WorkspacePanel.Visualizer) ui.State.SetVisualizerPlacement(VisualizerPlacement.Right);
        ui.FocusPanel(panel);
        ui.Key(InputKey.Parse("Ctrl+Enter"), 0);
        Assert.Equal(WorkspacePanel.Center, ui.State.Focus);
        Assert.Equal(PanelFocusArea.Tabs, ui.State.FocusArea);
        WorkspaceTab original = ui.State.ActiveTab;
        ui.Key(InputKey.Parse("Right"), 1);
        Assert.NotEqual(original, ui.State.ActiveTab);
        Assert.Equal(WorkspacePanel.Center, ui.State.Focus);
        ui.Key(InputKey.Parse("Enter"), 2);
        Assert.Equal(PanelFocusArea.Content, ui.State.FocusArea);
        Assert.Equal(WorkspaceOverlay.None, ui.Overlay);
        Assert.Empty(ui.State.Chat.Messages);
    }

    [Fact]
    public void HundredsOfPanelCyclesDoNotLeakTabOrTextState()
    {
        using GraphicalWorkspaceController ui = Create();
        for (int i = 0; i < 300; i++)
        {
            ui.Key(InputKey.Parse("Ctrl+Tab"), i * 3);
            Assert.Equal(WorkspacePanel.Center, ui.State.Focus);
            ui.Key(InputKey.Parse("Ctrl+Enter"), i * 3 + 1);
            ui.Key(InputKey.Parse("Esc"), i * 3 + 2);
            ui.Key(InputKey.Parse("Ctrl+Shift+Tab"), i * 3 + 3);
            Assert.Equal(WorkspacePanel.Explorer, ui.State.Focus);
            Assert.Equal(KeyboardScope.Explorer, ui.InputScope);
            Assert.Equal(PanelFocusArea.Content, ui.State.FocusArea);
        }
    }

    [Fact]
    public async Task ExplorerDirectionsFollowTreeSemanticsWithoutOpeningFiles()
    {
        string root = Path.Combine(Path.GetTempPath(), "oryn-tree-keys-" + Guid.NewGuid());
        string parent = Directory.CreateDirectory(Path.Combine(root, "folder")).FullName;
        string file = Path.Combine(parent, "child.txt"); File.WriteAllText(file, "preview");
        try
        {
            LocalWorkspaceFiles files = new(root, new()); WorkspaceState state = new(root);
            state.LoadRoot(await files.ListAsync(root, default));
            using GraphicalWorkspaceController ui = new(state, files, new(new()), Path.Combine(root, "keys.json"));
            ui.Key(InputKey.Parse("Right"), 0);
            for (int i = 0; i < 100 && ui.Loading; i++) { await Task.Delay(5); ui.Tick(0); }
            Assert.False(ui.Loading); Assert.Equal(2, state.Rows.Count); Assert.Equal(0, state.SelectedIndex);
            ui.Key(InputKey.Parse("Right"), 1);
            Assert.Equal(file, state.Selected?.Entry.Path); Assert.Contains(parent, state.ExpandedDirectories);
            ui.Key(InputKey.Parse("Right"), 2);
            Assert.Null(state.Preview); Assert.Equal(WorkspacePanel.Explorer, state.Focus);
            ui.Key(InputKey.Parse("Left"), 3);
            Assert.Equal(0, state.SelectedIndex); Assert.Single(state.Rows);
            Assert.Empty(state.ExpandedDirectories);
            ui.Key(InputKey.Parse("Left"), 4);
            Assert.Single(state.Rows); Assert.Empty(state.ExpandedDirectories);
            ui.Key(InputKey.Parse("Left"), 5);
            Assert.Single(state.Rows); Assert.Equal(WorkspacePanel.Explorer, state.Focus);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(WorkspaceOverlay.Menu)]
    [InlineData(WorkspaceOverlay.Options)]
    [InlineData(WorkspaceOverlay.Details)]
    [InlineData(WorkspaceOverlay.ElementChat)]
    [InlineData(WorkspaceOverlay.Keybindings)]
    public void ModalScopesCannotChangePanelsTabsOrExplorerSelection(WorkspaceOverlay overlay)
    {
        using GraphicalWorkspaceController ui = Create();
        if (overlay == WorkspaceOverlay.Keybindings) ui.OpenBindings(); else ui.OpenOverlay(overlay);
        foreach (string chord in new[] { "Ctrl+Tab", "Ctrl+Shift+Tab", "Ctrl+Enter", "F1", "F2", "F3", "F4", "Right", "Left" })
            ui.Key(InputKey.Parse(chord), 0);
        Assert.Equal(WorkspacePanel.Explorer, ui.State.Focus);
        Assert.Equal(WorkspaceTab.Chat, ui.State.ActiveTab);
        Assert.Equal(overlay, ui.Overlay);
        ui.Key(InputKey.Parse("Esc"), 1); Assert.Equal(WorkspaceOverlay.None, ui.Overlay);
    }

    [Fact]
    public void TextCommandsAreConfigurableAndUseTheSameConflictValidation()
    {
        KeyBindingSettings keys = new(); keys.Text["TextSubmit"] = ["Ctrl+S"];
        using GraphicalWorkspaceController ui = Create(keys); ui.Execute(KeyboardAction.FocusChat, 0);
        ui.Text("hello"); ui.Key(InputKey.Parse("Enter"), 1); Assert.Empty(ui.State.Chat.Messages);
        ui.Key(InputKey.Parse("Ctrl+S"), 2); Assert.NotEmpty(ui.State.Chat.Messages);
        keys.Text["TextLeft"] = ["Ctrl+Tab"];
        Assert.Throws<ArgumentException>(() => new KeyboardRouter(keys));
    }

    [Fact]
    public void RepeatCannotConfirmATabThenSubmitOrEnterADialog()
    {
        using GraphicalWorkspaceController ui = Create();
        ui.Key(InputKey.Parse("Ctrl+Enter"), 0); ui.Key(InputKey.Parse("Enter"), 1);
        ui.Text("unsent"); ui.Key(InputKey.Parse("Enter"), 2, repeat: true);
        Assert.Empty(ui.State.Chat.Messages);
        ui.OpenOverlay(WorkspaceOverlay.Menu); ui.Key(InputKey.Parse("Enter"), 3);
        Assert.Equal(WorkspaceOverlay.Keybindings, ui.Overlay);
        ui.Key(InputKey.Parse("Enter"), 4, repeat: true);
        Assert.Equal(WorkspaceOverlay.Keybindings, ui.Overlay);
    }

    private static GraphicalWorkspaceController Create(KeyBindingSettings? keys = null) =>
        new(new("/tmp"), new LocalWorkspaceFiles("/tmp", new()), new(keys ?? new()), "/tmp/unused-oryn-scoped-keys");
}
