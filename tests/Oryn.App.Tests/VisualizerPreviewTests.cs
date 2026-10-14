using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class VisualizerPreviewTests
{
    [Fact]
    public void MembersHaveStableUniqueIdentitiesAndNavigationStaysWithinTheirParent()
    {
        VisualizerPreviewState graph = new(), another = new();
        Guid[] identities = graph.Nodes.SelectMany(node => node.Members.Select(member => member.Id).Prepend(node.Id)).ToArray();
        Assert.Equal(identities.Length, identities.Distinct().Count());
        Assert.Equal(identities, another.Nodes.SelectMany(node => node.Members.Select(member => member.Id).Prepend(node.Id)));
        graph.ChangeZoom(-1);
        Guid parent = graph.SelectedId;
        Assert.True(graph.EnterComponent());
        Assert.Equal(PreviewZoom.Details, graph.Zoom);
        Assert.Equal(parent, graph.Target.ParentId);
        Assert.False(graph.EnterComponent());
        graph.Move(0, 99);
        Assert.Equal(graph.Selected.Members.Last().Id, graph.Target.Id);
        Assert.Equal(parent, graph.SelectedId);
        Assert.False(graph.SelectMember(Guid.NewGuid()));
        graph.Move(0, -99);
        Assert.Equal(graph.Selected.Members.First().Id, graph.Target.Id);
        graph.ExitComponent();
        Assert.Null(graph.Target.ParentId);
        Assert.Equal(parent, graph.Target.Id);
        Assert.Equal(PreviewZoom.Overview, graph.Zoom);
    }

    [Fact]
    public void ComponentChatsRetainHistoryByIdentityWithoutSharingParentOrSameNamedMember()
    {
        VisualizerPreviewState graph = new();
        PreviewGraphNode files = graph.Nodes.First(node => node.Name == "WorkspaceFiles");
        PreviewGraphNode contract = graph.Nodes.First(node => node.Name == "IWorkspaceFiles");
        ChatPreviewState chat = graph.ChatFor(files.Members[0].Id);
        chat.Draft = "Explain this function";
        Assert.True(chat.Send());
        chat.CompleteReply();
        chat.Draft = "Unsent draft";
        Assert.Same(chat, graph.ChatFor(files.Members[0].Id));
        Assert.Equal(2, chat.Messages.Count);
        Assert.Equal("Unsent draft", graph.ChatFor(files.Members[0].Id).Draft);
        Assert.Empty(graph.ChatFor(files.Id).Messages);
        Assert.Empty(graph.ChatFor(contract.Members[0].Id).Messages);
        Assert.Throws<ArgumentException>(() => graph.ChatFor(Guid.NewGuid()));
    }

    [Fact]
    public void ComponentShortcutsAreScopedAndAltSuperStillWorksOutsideGraph()
    {
        KeyboardContext graph = new(WorkspacePanel.Center, PanelFocusArea.Content, GraphActive: true);
        KeyboardRouter router = new(new());
        Assert.Equal(KeyboardAction.GraphEnterComponent, router.Route(InputKey.Parse("Enter"), graph, 0).Action);
        Assert.Equal(KeyboardAction.GraphExitComponent, router.Route(InputKey.Parse("Esc"), graph, 0).Action);
        Assert.Equal(KeyboardAction.GraphOptions, router.Route(InputKey.Parse("Alt+Enter"), graph, 0).Action);
        Assert.False(router.Route(InputKey.Parse("Alt+Enter"), graph with { GraphActive = false }, 0).Handled);
        KeyboardRouter alt = new(new() { Super = "Alt" });
        Assert.Equal(KeyboardAction.GraphOptions, alt.Route(InputKey.Parse("Alt+Enter"), graph, 0).Action);
        Assert.Equal(KeyboardAction.ToggleTabFocus, alt.Route(InputKey.Parse("Alt+Enter"), graph with { GraphActive = false }, 0).Action);
        Assert.Equal(KeyboardAction.ToggleTabFocus, alt.Route(InputKey.Parse("F6"), graph, 0).Action);
        Assert.Equal(KeyboardAction.ToggleTabFocus, alt.Route(InputKey.Parse("Alt+Enter"), graph with { Area = PanelFocusArea.Tabs }, 0).Action);
    }

    [Fact]
    public void FixtureHasStableIdentitiesAndOnlyLinksToKnownNodes()
    {
        VisualizerPreviewState graph = new(), another = new();
        Assert.Equal(graph.Nodes.Select(node => node.Id), another.Nodes.Select(node => node.Id));
        Assert.Equal(graph.Nodes.Count, graph.Nodes.Select(node => node.Id).Distinct().Count());
        Assert.All(graph.Links, link => { Assert.Contains(graph.Nodes, node => node.Id == link.From); Assert.Contains(graph.Nodes, node => node.Id == link.To); });
        Assert.Equal(4, graph.Nodes.Select(node => node.Status).Distinct().Count());
    }

    [Fact]
    public void NavigationAndZoomKeepSelectionAndDoNotAlterFixtureStates()
    {
        VisualizerPreviewState graph = new();
        graph.Move(1, 0);
        Assert.Equal("WorkspaceState", graph.Selected.Name);
        graph.Move(0, 1);
        Assert.Equal("ChatPreviewState", graph.Selected.Name);
        Guid selected = graph.SelectedId;
        graph.ChangeZoom(20); Assert.Equal(PreviewZoom.Details, graph.Zoom);
        graph.ChangeZoom(-20); Assert.Equal(PreviewZoom.Overview, graph.Zoom);
        Assert.Equal(selected, graph.SelectedId);
        graph.ResetZoom(); Assert.Equal(PreviewZoom.Standard, graph.Zoom);
        graph.Select(Guid.NewGuid()); Assert.Equal(selected, graph.SelectedId);
        Assert.Equal(PreviewDesignStatus.InProgress, graph.Selected.Status);
    }

    [Fact]
    public void ViewportFollowsSelectionAndReturnsInsideBoundsAfterResize()
    {
        VisualizerPreviewState graph = new();
        PreviewGraphLayout layout = new(PreviewZoom.Details, 24);
        graph.Select(graph.Nodes.First(node => node.Name == "Visualizer").Id);
        graph.EnsureVisible(layout.Bounds(graph.Selected), 24, 10, layout.Width, layout.Height);
        Assert.True(graph.ScrollX > 0); Assert.True(graph.ScrollY > 0);
        PanelBounds selected = layout.Bounds(graph.Selected);
        Assert.InRange(selected.X, graph.ScrollX, graph.ScrollX + 24 - selected.Width);
        graph.EnsureVisible(selected, 500, 500, layout.Width, layout.Height);
        Assert.Equal(0, graph.ScrollX); Assert.Equal(0, graph.ScrollY);
    }

    [Fact]
    public void GraphShortcutsAreScopedAndCannotConflictWithActiveContent()
    {
        KeyboardRouter router = new(new());
        KeyboardContext content = new(WorkspacePanel.Center, PanelFocusArea.Content);
        Assert.False(router.Route(InputKey.Parse("PageUp"), content, 0).Handled);
        Assert.Equal(KeyboardAction.GraphZoomIn, router.Route(InputKey.Parse("PageUp"), content with { GraphActive = true }, 0).Action);
        Assert.False(router.Route(InputKey.Parse("PageUp"), content with { GraphActive = true, Area = PanelFocusArea.Tabs }, 0).Handled);
        Assert.Throws<ArgumentException>(() => new KeyboardRouter(new() { Graph = new() { ["GraphZoomIn"] = ["Right"] } }));
        Assert.Throws<ArgumentException>(() => new KeyboardRouter(new() { Graph = new() { ["GraphZoomIn"] = ["Ctrl+Enter"] } }));
    }
}
