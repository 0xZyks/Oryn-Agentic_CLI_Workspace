using System.Numerics;
using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class GraphDragTests
{
    private static PreviewGraphNode Node(int col, int row) => new(Guid.NewGuid(), "card", "fixture", PreviewDesignStatus.ToValidate, col, row, []);
    private static void Clear(GraphBox moving, GraphBox other) => Assert.False(moving.X < other.Right - .01f && moving.Right > other.X + .01f
        && moving.Y < other.Bottom - .01f && moving.Bottom > other.Y + .01f, "Cards overlap while moving");

    [Theory]
    [InlineData(-70)]
    [InlineData(70)]
    public void SweptMotionContoursObstacleOnShorterSideAndRejoinsPointer(int horizontal)
    {
        PreviewGraphNode card = Node(1, 0), obstacle = Node(1, 1);
        GraphDragState drag = new(); drag.Configure([card, obstacle], new(PreviewZoom.Standard, 100));
        GraphBox initial = drag.Bounds(card.Id), blocked = drag.Bounds(obstacle.Id);
        Vector2 grab = new(30, 20), target = new(initial.X + horizontal, blocked.Bottom + 80);
        Assert.True(drag.Begin(card.Id, new Vector2(initial.X, initial.Y) + grab)); drag.Target(target + grab);
        bool passedSide = false;
        for (int i = 0; i < 120; i++)
        {
            drag.Advance(1f / 60); GraphBox current = drag.Bounds(card.Id); Clear(current, blocked);
            if (current.Y < blocked.Bottom && current.Bottom > blocked.Y)
                passedSide |= horizontal < 0 ? current.Right <= blocked.X : current.X >= blocked.Right;
        }
        Assert.True(passedSide);
        Assert.InRange(Vector2.Distance(target, new(drag.Bounds(card.Id).X, drag.Bounds(card.Id).Y)), 0, .01f);
        Assert.Equal(blocked, drag.Bounds(obstacle.Id));
    }

    [Fact]
    public void CursorInsideObstacleStartsSlidingAndDoesNotOscillateAtItsCenter()
    {
        PreviewGraphNode card = Node(1, 0), obstacle = Node(1, 1);
        GraphDragState drag = new(); drag.Configure([card, obstacle], new(PreviewZoom.Standard, 100));
        GraphBox initial = drag.Bounds(card.Id), blocked = drag.Bounds(obstacle.Id);
        drag.Begin(card.Id, new(initial.X, initial.Y));
        for (int i = 0; i < 100; i++)
        {
            drag.Target(new(initial.X + (i % 2 == 0 ? -1 : 1), blocked.Y)); drag.Advance(1f / 60);
            Clear(drag.Bounds(card.Id), blocked);
        }
        Assert.True(drag.Bounds(card.Id).Right <= blocked.X);
    }

    [Fact]
    public void ApproachFollowsPointerExactlyUntilCardContactThenContours()
    {
        PreviewGraphNode card = Node(1, 0), obstacle = Node(1, 1);
        GraphDragState drag = new(); drag.Configure([card, obstacle], new(PreviewZoom.Standard, 100));
        GraphBox initial = drag.Bounds(card.Id), blocked = drag.Bounds(obstacle.Id);
        Vector2 grab = new(70, 35); drag.Begin(card.Id, new Vector2(initial.X, initial.Y) + grab);
        float contactY = blocked.Y - initial.Height - GraphDragState.Clearance;
        Vector2 free = new(initial.X, contactY - 1);
        drag.Target(free + grab);
        Assert.Equal(free.X, drag.Bounds(card.Id).X); Assert.Equal(free.Y, drag.Bounds(card.Id).Y);
        Assert.False(drag.Moving);
        for (int i = 0; i < 20; i++) drag.Advance(1f / 60);
        Assert.Equal(free.X, drag.Bounds(card.Id).X); Assert.Equal(free.Y, drag.Bounds(card.Id).Y);
        drag.Target(new Vector2(initial.X, blocked.Bottom + 100) + grab);
        Assert.Equal(initial.X, drag.Bounds(card.Id).X);
        Assert.InRange(Math.Abs(drag.Bounds(card.Id).Y - contactY), 0, .01f);
        drag.Advance(1f / 60); Clear(drag.Bounds(card.Id), blocked);
        Assert.NotEqual(initial.X, drag.Bounds(card.Id).X);
        drag.Target(free + grab); // Retreat immediately regains the grab offset, with no sticky side.
        Assert.Equal(free.X, drag.Bounds(card.Id).X); Assert.Equal(free.Y, drag.Bounds(card.Id).Y);
        Assert.False(drag.Moving);
    }

    [Fact]
    public void ReleaseKeepsPositionCancelRestoresItAndZoomPreservesSeparation()
    {
        PreviewGraphNode card = Node(0, 0), obstacle = Node(1, 0);
        GraphDragState drag = new(); drag.Configure([card, obstacle], new(PreviewZoom.Standard, 100));
        GraphBox initial = drag.Bounds(card.Id); drag.Begin(card.Id, new(initial.X + 20, initial.Y + 20));
        drag.Target(new(initial.X + 20, initial.Y + 120)); drag.Advance(.05f);
        GraphBox moved = drag.Bounds(card.Id); drag.End(); drag.Advance(1);
        Assert.Equal(moved, drag.Bounds(card.Id)); Assert.Null(drag.DraggedId);
        drag.Begin(card.Id, new(moved.X, moved.Y)); drag.Target(new(1000, 1000)); drag.Advance(.05f); drag.End(cancel: true);
        Assert.Equal(moved, drag.Bounds(card.Id));
        foreach (PreviewZoom zoom in Enum.GetValues<PreviewZoom>())
        { drag.Configure([card, obstacle], new(zoom, 100)); Clear(drag.Bounds(card.Id), drag.Bounds(obstacle.Id)); }
        GraphDragState another = new(); another.Configure([card, obstacle], new(PreviewZoom.Standard, 100));
        Assert.Equal(initial, another.Bounds(card.Id));
    }

    [Fact]
    public void MultipleObstaclesAndLargePointerJumpsNeverTunnelOrChangeGraphIdentities()
    {
        VisualizerPreviewState graph = new(); GraphDragState drag = graph.Placement;
        drag.Configure(graph.Nodes, new(PreviewZoom.Standard, 100));
        var links = graph.Links.ToArray(); Guid[] ids = graph.Nodes.Select(n => n.Id).ToArray();
        GraphBox before = drag.Bounds(ids[0]); drag.Begin(ids[0], new(before.X, before.Y)); drag.Target(new(900, 800));
        for (int i = 0; i < 200; i++)
        { drag.Advance(.05f); foreach (Guid id in ids.Skip(1)) Clear(drag.Bounds(ids[0]), drag.Bounds(id)); }
        Assert.Equal(links, graph.Links); Assert.Equal(ids, graph.Nodes.Select(n => n.Id));
        Assert.InRange(Vector2.Distance(new(900, 800), new(drag.Bounds(ids[0]).X, drag.Bounds(ids[0]).Y)), 0, .01f);
    }
}
