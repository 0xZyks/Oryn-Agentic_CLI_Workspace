using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class NavbarTests
{
    private static GraphicalWorkspaceController Create(KeyBindingSettings? keys = null) =>
        new(new("/tmp"), new LocalWorkspaceFiles("/tmp", new()), new(keys ?? new()), "/tmp/unused-navbar-keys");

    [Theory]
    [InlineData(WorkspacePanel.Explorer)]
    [InlineData(WorkspacePanel.Center)]
    [InlineData(WorkspacePanel.Visualizer)]
    public void ShiftTabSelectsBarAndEnterOpensSelectedMenuWithoutActingOnThePane(WorkspacePanel panel)
    {
        using var ui = Create(); ui.FocusPanel(panel);
        var beforeFocus = ui.State.Focus; var beforeTab = ui.State.ActiveTab;
        ui.Key(InputKey.Parse("Shift+Tab"), 0);
        Assert.True(ui.NavbarFocused); Assert.False(ui.AcceptsText);
        Assert.Equal(KeyboardScope.Navbar, ui.InputScope); Assert.Equal(WorkspaceOverlay.None, ui.Overlay);
        ui.Key(InputKey.Parse("Right"), 1); ui.Key(InputKey.Parse("Right"), 2);
        Assert.Equal(2, ui.NavbarIndex); Assert.Equal(beforeFocus, ui.State.Focus); Assert.Equal(beforeTab, ui.State.ActiveTab);
        ui.Key(InputKey.Parse("Enter"), 3);
        Assert.Equal(WorkspaceOverlay.Menu, ui.Overlay); Assert.Equal("View", ui.MenuTitle);
        Assert.Equal(KeyboardAction.ToggleDock, ui.MenuItems[0].Action);
        ui.Key(InputKey.Parse("Esc"), 4); Assert.True(ui.NavbarFocused);
        ui.Key(InputKey.Parse("Esc"), 5); Assert.False(ui.NavbarFocused);
        Assert.Equal(beforeFocus, ui.State.Focus); Assert.Equal(beforeTab, ui.State.ActiveTab);
    }

    [Fact]
    public void SelectionWrapsAndHeldConfirmationCannotExecuteTheMenuItem()
    {
        using var ui = Create(); ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("draft");
        ui.Key(InputKey.Parse("Shift+Tab"), 1);
        ui.Key(InputKey.Parse("Shift+Tab"), 2, repeat: true); Assert.True(ui.NavbarFocused);
        ui.Key(InputKey.Parse("Left"), 3); Assert.Equal(5, ui.NavbarIndex);
        ui.Key(InputKey.Parse("Enter"), 4); Assert.Equal("Settings", ui.MenuTitle);
        ui.Key(InputKey.Parse("Enter"), 5, repeat: true); Assert.Equal(WorkspaceOverlay.Menu, ui.Overlay);
        ui.Key(InputKey.Parse("Enter"), 6); Assert.Equal(WorkspaceOverlay.Keybindings, ui.Overlay);
        Assert.Equal("draft", ui.State.Chat.Draft); Assert.Empty(ui.State.Chat.Messages);
    }

    [Fact]
    public void NavbarOwnsTypingAndRestoresChatHistoryOrTabFocusExactly()
    {
        using var ui = Create(); ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("draft");
        ui.Editor.Place(2); ui.FocusHistory(true); ui.Composition = "é"; ui.Key(InputKey.Parse("Shift+Tab"), 1); ui.Text("ignored"); Assert.Equal("", ui.Composition);
        ui.Key(InputKey.Parse("Down"), 2); Assert.Equal(0, ui.ChatScroll);
        ui.Key(InputKey.Parse("Shift+Tab"), 3); Assert.True(ui.HistoryFocused); Assert.False(ui.NavbarFocused);
        Assert.Equal("draft", ui.State.Chat.Draft); Assert.Equal(2, ui.Editor.Cursor);
        ui.Key(InputKey.Parse("Ctrl+Enter"), 4); ui.Key(InputKey.Parse("Shift+Tab"), 5); ui.Key(InputKey.Parse("Esc"), 6);
        Assert.Equal(PanelFocusArea.Tabs, ui.State.FocusArea);
    }

    [Fact]
    public void MenusHaveTheirOwnCountsAndUnavailableEntriesDoNotRunCommands()
    {
        using var ui = Create(); ui.OpenNavbarMenu(0);
        ui.Key(InputKey.Parse("Down"), 0); Assert.Equal(0, ui.OverlaySelection);
        Assert.Equal("File", ui.MenuTitle); Assert.Single(ui.MenuItems);
        ui.OpenNavbarMenu(1); ui.Key(InputKey.Parse("Enter"), 1);
        Assert.Equal(WorkspaceOverlay.Menu, ui.Overlay); Assert.False(ui.State.QuitRequested);
        ui.OpenOverlay(WorkspaceOverlay.Menu); Assert.Equal(6, ui.MenuItems.Count);
        Assert.Equal("Workspace", ui.MenuTitle); ui.Key(InputKey.Parse("Enter"), 2);
        Assert.Equal(WorkspaceOverlay.Keybindings, ui.Overlay);
    }

    [Fact]
    public void AltSuperProfilesKeepPanelAccessAndExplicitConflictsAreRejected()
    {
        using var ui = Create(new() { Super = "Alt" });
        Assert.Equal("Ctrl+Tab", ui.Keyboard.DisplayHint(KeyboardAction.NextPanel));
        ui.Key(InputKey.Parse("Ctrl+Tab"), 0); Assert.Equal(WorkspacePanel.Center, ui.State.Focus);
        ui.Key(InputKey.Parse("Shift+Tab"), 1); Assert.True(ui.NavbarFocused);
        KeyBindingSettings bad = new(); bad.Panels["NextPanel"] = ["Shift+Tab"];
        Assert.Throws<ArgumentException>(() => new KeyboardRouter(bad));
        KeyBindingSettings remapped = new(); remapped.Navbar["ToggleNavbarFocus"] = ["Ctrl+M"];
        using var other = Create(remapped); other.Key(InputKey.Parse("Ctrl+M"), 0); Assert.True(other.NavbarFocused);
    }

    [Fact]
    public void OtherModalContextsKeepShiftTabIsolated()
    {
        using var ui = Create(); ui.OpenOverlay(WorkspaceOverlay.Options);
        ui.Key(InputKey.Parse("Shift+Tab"), 0); Assert.False(ui.NavbarFocused); Assert.Equal(WorkspaceOverlay.Options, ui.Overlay);
    }
    [Fact]
    public void FormerNavbarAndLocalDefaultsMigrateWithoutRewritingTheSavedProfile()
    {
        string root = Path.Combine(Path.GetTempPath(), "oryn-navbar-profile-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        string path = Path.Combine(root, "keys.json");
        try
        {
            string original = """{"Navbar":{"ToggleNavbarFocus":["Alt+Tab","F9"],"PreviousNavbar":["Left","Shift+Tab"]},"Tabs":{"PreviousTab":["Left","Shift+Tab"]},"Content":{"ToggleHistory":["Tab","Shift+Tab"]}}""";
            File.WriteAllText(path, original); KeyboardRouter router = new(KeyBindingStore.Load(path));
            foreach (KeyboardScope scope in new[] { KeyboardScope.Explorer, KeyboardScope.TextEditor, KeyboardScope.History, KeyboardScope.Tabs, KeyboardScope.Graph, KeyboardScope.Navbar })
                Assert.Equal(KeyboardAction.ToggleNavbarFocus, router.Route(InputKey.Parse("Shift+Tab"), new(WorkspacePanel.Center, PanelFocusArea.Content, Scope: scope), 0).Action);
            Assert.Equal(original, File.ReadAllText(path));
            File.WriteAllText(path, """{"Navbar":{"ToggleNavbarFocus":["Ctrl+M"]}}""");
            Assert.Equal(new[] { "Ctrl+M" }, KeyBindingStore.Load(path).Navbar["ToggleNavbarFocus"]);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void AltTabNoLongerChangesTheWorkspaceWhileModalShiftTabStillTraversesLocally()
    {
        using var ui = Create(); ui.Execute(KeyboardAction.FocusChat, 0);
        Assert.False(ui.Key(InputKey.Parse("Alt+Tab"), 1).Handled); Assert.True(ui.AcceptsText);
        ui.OpenBindings(); ui.Key(InputKey.Parse("Tab"), 2); Assert.Equal(0, ui.BindingIndex);
        ui.Key(InputKey.Parse("Shift+Tab"), 3); Assert.Equal(-1, ui.BindingIndex);
        Assert.False(ui.NavbarFocused); Assert.Equal(WorkspaceOverlay.Keybindings, ui.Overlay);
    }

}
