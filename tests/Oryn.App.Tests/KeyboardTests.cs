using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class KeyboardTests
{
    private static KeyboardContext Explorer => new(WorkspacePanel.Explorer, PanelFocusArea.Content);
    private static KeyboardContext Tabs => new(WorkspacePanel.Center, PanelFocusArea.Tabs);
    private static KeyboardContext Content => new(WorkspacePanel.Center, PanelFocusArea.Content);

    [Fact]
    public void OnlySuperTabTraversesPanelsWhileOrdinaryTabBelongsToThePanel()
    {
        KeyboardRouter router = new(new());
        Assert.Equal(KeyboardAction.NextPanel, router.Route(InputKey.Parse("Ctrl+Tab"), Explorer, 0).Action);
        foreach (KeyboardContext context in new[] { Explorer, Tabs, Content })
        {
            Assert.Equal(context == Tabs, router.Route(InputKey.Parse("Tab"), context, 0).Handled);
            Assert.Equal(KeyboardAction.ToggleNavbarFocus, router.Route(InputKey.Parse("Shift+Tab"), context, 0).Action);
            Assert.Equal(KeyboardAction.PreviousPanel, router.Route(InputKey.Parse("Ctrl+Shift+Tab"), context, 0).Action);
        }
        KeyboardContext editing = Content with { ContentAcceptsTab = true, ContentAcceptsText = true };
        Assert.False(router.Route(InputKey.Parse("Tab"), editing, 0).Handled);
        Assert.Equal(KeyboardAction.ToggleNavbarFocus, router.Route(InputKey.Parse("Shift+Tab"), editing, 0).Action);
        Assert.Equal(KeyboardAction.NextPanel, router.Route(InputKey.Parse("Ctrl+Tab"), editing, 0).Action);
    }

    [Fact]
    public void ArrowsHaveDistinctTabExplorerAndContentScopes()
    {
        KeyboardRouter router = new(new());
        Assert.Equal(KeyboardAction.NextTab, router.Route(InputKey.Parse("Right"), Tabs, 0).Action);
        Assert.Equal(KeyboardAction.ExpandSelection, router.Route(InputKey.Parse("Right"), Explorer, 0).Action);
        Assert.Equal(KeyboardAction.ContentRight, router.Route(InputKey.Parse("Right"), Content, 0).Action);
        Assert.Equal(KeyboardAction.ResizeLarger, router.Route(InputKey.Parse("Ctrl+Right"), Tabs, 0).Action);
        Assert.Equal(KeyboardAction.ToggleTabFocus, router.Route(InputKey.Parse("Ctrl+Enter"), Content, 0).Action);
        Assert.Equal(KeyboardAction.ToggleTabFocus, router.Route(InputKey.Parse("Ctrl+Enter"), Explorer, 0).Action);
    }

    [Fact]
    public void EnterConfirmsOnlyTheSelectedTabAndKeepsContentEnterAvailable()
    {
        KeyboardRouter router = new(new());
        Assert.Equal(KeyboardAction.ConfirmTab, router.Route(InputKey.Parse("Enter"), Tabs, 0).Action);
        Assert.False(router.Route(InputKey.Parse("Enter"), Content, 0).Handled);
        Assert.Equal(KeyboardAction.OpenSelection, router.Route(InputKey.Parse("Enter"), Explorer, 0).Action);
        WorkspaceState state = new(Path.GetTempPath());
        state.Dispatch(WorkspaceCommand.ChatTab);
        state.Dispatch(WorkspaceCommand.ToggleTabFocus);
        state.Dispatch(WorkspaceCommand.NextTab);
        state.Dispatch(WorkspaceCommand.ConfirmTab);
        Assert.Equal(WorkspaceTab.Code, state.ActiveTab);
        Assert.Equal(PanelFocusArea.Content, state.FocusArea);
        state.Dispatch(WorkspaceCommand.ToggleTabFocus);
        state.Dispatch(WorkspaceCommand.ToggleTabFocus); // Super+Enter confirmation fallback stays valid.
        Assert.Equal(PanelFocusArea.Content, state.FocusArea);
    }

    [Fact]
    public void SpaceLeaderHasTimeoutCancellationAndDoesNotStealTextSpaces()
    {
        KeyboardRouter router = new(new() { Super = "Space" });
        Assert.True(router.Route(InputKey.Parse("Space"), Content, 0).Handled);
        Assert.True(router.LeaderPending);
        Assert.Equal(KeyboardAction.ToggleTabFocus, router.Route(InputKey.Parse("Enter"), Content, 10).Action);
        Assert.False(router.LeaderPending);
        router.Route(InputKey.Parse("Space"), Content, 20);
        Assert.True(router.Route(InputKey.Parse("Esc"), Content, 30).Handled);
        Assert.False(router.LeaderPending);
        router.Route(InputKey.Parse("Space"), Explorer, 100);
        Assert.True(router.Expire(1100));
        Assert.Equal(KeyboardAction.OpenSelection, router.Route(InputKey.Parse("Enter"), Explorer, 1101).Action);
        router.Route(InputKey.Parse("Space"), Explorer, 1200);
        KeyboardContext editing = Content with { ContentAcceptsText = true };
        Assert.False(router.Route(InputKey.Parse("Space"), editing, 1201).Handled);
        Assert.False(router.LeaderPending);
        router.Route(InputKey.Parse("Space"), Content, 1300);
        Assert.True(router.Route(InputKey.Parse("Z"), Content, 1301).Handled);
        Assert.False(router.LeaderPending);
    }

    [Fact]
    public void AltAndCustomLeaderResolveSuperBindings()
    {
        Assert.Equal(KeyboardAction.ToggleNavbarFocus, new KeyboardRouter(new() { Super = "Alt" })
            .Route(InputKey.Parse("Shift+Tab"), Content, 0).Action);
        Assert.Equal(KeyboardAction.NextPanel, new KeyboardRouter(new() { Super = "Alt" })
            .Route(InputKey.Parse("Ctrl+Tab"), Content, 0).Action);
        KeyboardRouter leader = new(new() { Super = "F12" });
        leader.Route(InputKey.Parse("F12"), Content, 0);
        Assert.Equal(KeyboardAction.ResizeLarger, leader.Route(InputKey.Parse("Right"), Content, 1).Action);
    }

    [Fact]
    public void ConflictsAreCheckedAcrossOverlappingScopes()
    {
        KeyBindingSettings profile = new();
        profile.Workspace["Quit"] = ["Right"];
        Assert.Throws<ArgumentException>(() => new KeyboardRouter(profile));
        profile = new();
        profile.Panels["NextPanel"] = ["Ctrl+J"];
        Assert.Equal(KeyboardAction.NextPanel, new KeyboardRouter(profile).Route(InputKey.Parse("Ctrl+J"), Explorer, 0).Action);
        Assert.Throws<ArgumentException>(() => new KeyboardRouter(new() { Super = "Shift" })); // Next/previous collapse to the same Shift+Tab.
        profile.Explorer["Quit"] = ["F9"];
        Assert.Throws<ArgumentException>(() => new KeyboardRouter(profile));
    }

    [Fact]
    public void LoadingOldPanelDefaultsReleasesTabWithoutRewritingTheProfile()
    {
        string directory = Path.Combine(Path.GetTempPath(), "oryn-tab-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "keys.json");
        try
        {
            string original = """{"Panels":{"NextPanel":["Super+Tab","Tab"],"PreviousPanel":["Super+Shift+Tab","Shift+Tab"]}}""";
            File.WriteAllText(path, original);
            KeyBindingSettings loaded = KeyBindingStore.Load(path);
            Assert.Equal(new[] { "Super+Tab" }, loaded.Panels["NextPanel"]);
            Assert.Equal(new[] { "Super+Shift+Tab" }, loaded.Panels["PreviousPanel"]);
            Assert.Equal(original, File.ReadAllText(path));
            original = """{"Panels":{"NextPanel":["Super+J","Tab"]}}""";
            File.WriteAllText(path, original);
            Assert.Throws<ArgumentException>(() => KeyBindingStore.Load(path)); // Tab also has local actions; ambiguous profiles are rejected.
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void EscapeCancelsResizeBeforeLeavingTabs()
    {
        KeyboardRouter router = new(new());
        Assert.Equal(KeyboardAction.CancelResize, router.Route(InputKey.Parse("Esc"), Tabs with { Resizing = true }, 0).Action);
        Assert.Equal(KeyboardAction.ReturnToContent, router.Route(InputKey.Parse("Esc"), Tabs, 0).Action);
    }

    [Theory]
    [InlineData("Win")]
    [InlineData("Command")]
    [InlineData("")]
    [InlineData("Unknown")]
    public void UnsupportedOrMalformedSuperIsRejected(string super) =>
        Assert.Throws<ArgumentException>(() => new KeyboardRouter(new() { Super = super }));

    [Fact]
    public void EmptyCriticalBindingsAndMalformedChordsAreRejected()
    {
        KeyBindingSettings settings = new();
        settings.Workspace["Quit"] = [];
        Assert.Throws<ArgumentException>(() => new KeyboardRouter(settings));
        settings.Workspace["Quit"] = ["Ctrl+Nonsense"];
        Assert.Throws<ArgumentException>(() => new KeyboardRouter(settings));
        Assert.Throws<ArgumentException>(() => new KeyboardRouter(new() { LeaderTimeoutMilliseconds = 0 }));
    }

    [Fact]
    public void GroupResetDoesNotChangeOtherGroupsAndSaveRecoversOnRestart()
    {
        string directory = Path.Combine(Path.GetTempPath(), "oryn-bindings-" + Guid.NewGuid());
        string path = Path.Combine(directory, "keys.json");
        try
        {
            KeyBindingSettings settings = new() { Super = "Alt" };
            settings.Panels["NextPanel"] = ["Super+J"];
            settings.Content["ContentDown"] = ["J", "Down"];
            KeyBindingStore.Save(path, settings);
            KeyBindingSettings recovered = KeyBindingStore.Load(path);
            Assert.Equal("Alt", recovered.Super);
            Assert.Equal(KeyboardAction.NextPanel, new KeyboardRouter(recovered).Route(InputKey.Parse("Alt+J"), Explorer, 0).Action);
            recovered.Panels.Clear();
            Assert.Equal(KeyboardAction.NextPanel, new KeyboardRouter(recovered).Route(InputKey.Parse("Ctrl+Tab"), Explorer, 0).Action);
            Assert.Equal(KeyboardAction.ContentDown, new KeyboardRouter(recovered).Route(InputKey.Parse("J"), Content, 0).Action);
            string original = File.ReadAllText(path);
            Assert.Throws<ArgumentException>(() => KeyBindingStore.Save(path, recovered with { Super = "Win" }));
            Assert.Equal(original, File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void TabFocusIsIndependentOfRendererAndContentArrowsDoNotSwitchTabs()
    {
        WorkspaceState state = new(Path.GetTempPath());
        state.Dispatch(WorkspaceCommand.ToggleTabFocus);
        Assert.Equal(WorkspacePanel.Center, state.Focus);
        Assert.Equal(PanelFocusArea.Tabs, state.FocusArea);
        state.Dispatch(WorkspaceCommand.ChatTab);
        state.Dispatch(WorkspaceCommand.NextTab);
        Assert.Equal(WorkspaceTab.Chat, state.ActiveTab);
        state.Dispatch(WorkspaceCommand.ToggleTabFocus);
        state.Dispatch(WorkspaceCommand.NextTab);
        Assert.Equal(WorkspaceTab.Code, state.ActiveTab);
        Assert.Equal(PanelFocusArea.Tabs, state.FocusArea);
        state.Dispatch(WorkspaceCommand.NextTab);
        Assert.Equal(VisualizerPlacement.Tab, state.LayoutOptions.VisualizerPlacement);
        Assert.Equal(WorkspaceTab.Visualizer, state.ActiveTab);
        state.Dispatch(WorkspaceCommand.PreviousTab);
        Assert.Equal(WorkspaceTab.Code, state.ActiveTab);
        state.Dispatch(WorkspaceCommand.ReturnToContent);
        Assert.Equal(PanelFocusArea.Content, state.FocusArea);
        state.Dispatch(WorkspaceCommand.NextPanel);
        Assert.Equal(WorkspacePanel.Explorer, state.Focus);
        Assert.Equal(PanelFocusArea.Content, state.FocusArea);
    }
}
