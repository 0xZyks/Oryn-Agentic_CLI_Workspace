namespace Oryn.App;

public enum PreviewDesignStatus { ToValidate, Validated, InProgress, Implemented }
public enum PreviewZoom { Overview, Standard, Details }
public sealed record PreviewGraphMember(Guid Id, string Name, string Kind, PreviewDesignStatus Status);
public sealed record PreviewGraphTarget(Guid Id, Guid? ParentId, string Name, string Kind, PreviewDesignStatus Status);
public sealed record PreviewGraphNode(Guid Id, string Name, string Kind, PreviewDesignStatus Status,
    int Column, int Row, PreviewGraphMember[] Members);
public sealed record PreviewGraphLink(Guid From, Guid To, string Label);

/// <summary>Fictitious M0 fixture, not observations or decisions about the actual repository.</summary>
public sealed class VisualizerPreviewState
{
    private static Guid Id(int value) => Guid.Parse($"00000000-0000-4000-8000-{value:000000000000}");
    private static PreviewGraphMember Member(int id, string name, string kind, PreviewDesignStatus status) => new(Id(id), name, kind, status);
    public IReadOnlyList<PreviewGraphNode> Nodes { get; } = Array.AsReadOnly(new[] {
        new PreviewGraphNode(Id(1), "Program", "entry point", PreviewDesignStatus.Implemented, 0, 0,
            [Member(101, "Main(args)", "function", PreviewDesignStatus.Implemented), Member(102, "OpenWorkspace()", "function", PreviewDesignStatus.Validated)]),
        new PreviewGraphNode(Id(2), "WorkspaceState", "class", PreviewDesignStatus.InProgress, 1, 0,
            [Member(201, "Dispatch(command)", "method", PreviewDesignStatus.InProgress), Member(202, "Focus", "property", PreviewDesignStatus.Implemented)]),
        new PreviewGraphNode(Id(3), "SdlWorkspace", "class", PreviewDesignStatus.Validated, 2, 0,
            [Member(301, "Run()", "method", PreviewDesignStatus.Validated), Member(302, "Refresh()", "method", PreviewDesignStatus.InProgress)]),
        new PreviewGraphNode(Id(4), "WorkspaceFiles", "class", PreviewDesignStatus.Implemented, 0, 1,
            [Member(401, "ListAsync(path)", "method", PreviewDesignStatus.Implemented), Member(402, "ReadAsync(path)", "method", PreviewDesignStatus.Implemented)]),
        new PreviewGraphNode(Id(5), "ChatPreviewState", "class", PreviewDesignStatus.InProgress, 1, 1,
            [Member(501, "Send()", "method", PreviewDesignStatus.InProgress), Member(502, "Draft", "property", PreviewDesignStatus.Implemented)]),
        new PreviewGraphNode(Id(6), "Visualizer", "concept", PreviewDesignStatus.ToValidate, 2, 1,
            [Member(601, "RenderGraph()", "planned method", PreviewDesignStatus.ToValidate), Member(602, "SelectedNode", "planned property", PreviewDesignStatus.ToValidate)]),
        new PreviewGraphNode(Id(7), "IWorkspaceFiles", "interface", PreviewDesignStatus.Validated, 0, 2,
            [Member(701, "ListAsync(path)", "signature", PreviewDesignStatus.Validated), Member(702, "ReadAsync(path)", "signature", PreviewDesignStatus.Validated)])
    });
    public IReadOnlyList<PreviewGraphLink> Links { get; } = Array.AsReadOnly(new[] {
        new PreviewGraphLink(Id(1), Id(2), "starts"), new PreviewGraphLink(Id(2), Id(3), "renders"),
        new PreviewGraphLink(Id(1), Id(4), "loads"), new PreviewGraphLink(Id(2), Id(5), "owns"),
        new PreviewGraphLink(Id(3), Id(6), "draws"), new PreviewGraphLink(Id(4), Id(7), "implements")
    });
    public GraphDragState Placement { get; } = new();
    public Guid SelectedId { get; private set; } = Id(1);
    public PreviewGraphNode Selected => Nodes.First(node => node.Id == SelectedId);
    public Guid? SelectedMemberId { get; private set; }
    public PreviewGraphMember? SelectedMember => Selected.Members.FirstOrDefault(member => member.Id == SelectedMemberId);
    public PreviewGraphTarget Target => SelectedMember is { } member
        ? new(member.Id, SelectedId, member.Name, member.Kind, member.Status)
        : new(Selected.Id, null, Selected.Name, Selected.Kind, Selected.Status);
    private PreviewZoom _beforeMembers;
    private readonly Dictionary<Guid, ChatPreviewState> _chats = [];
    public PreviewZoom Zoom { get; private set; } = PreviewZoom.Standard;
    public int ScrollX { get; private set; }
    public int ScrollY { get; private set; }

    public void Select(Guid id)
    {
        if (Nodes.Any(node => node.Id == id)) { SelectedId = id; SelectedMemberId = null; }
    }

    public void Move(int dx, int dy)
    {
        if (SelectedMemberId is not null)
        {
            int index = Array.FindIndex(Selected.Members, member => member.Id == SelectedMemberId);
            int direction = dy != 0 ? dy : dx;
            SelectedMemberId = Selected.Members[Math.Clamp(index + direction, 0, Selected.Members.Length - 1)].Id;
            return;
        }
        var current = Placement.Position(Selected);
        PreviewGraphNode? next = Nodes.Where(node => dx != 0
                ? (Placement.Position(node).X - current.X) * dx > 0 : (Placement.Position(node).Y - current.Y) * dy > 0)
            .OrderBy(node => dx != 0
                ? Math.Abs(Placement.Position(node).Y - current.Y) * 10 + Math.Abs(Placement.Position(node).X - current.X)
                : Math.Abs(Placement.Position(node).X - current.X) * 10 + Math.Abs(Placement.Position(node).Y - current.Y)).FirstOrDefault();
        if (next is not null) SelectedId = next.Id;
    }

    public bool EnterComponent() => SelectedMemberId is null && Selected.Members.Length > 0 && SelectMember(Selected.Members[0].Id);
    public bool SelectMember(Guid id)
    {
        if (!Selected.Members.Any(member => member.Id == id)) return false;
        if (SelectedMemberId is null) _beforeMembers = Zoom;
        SelectedMemberId = id;
        Zoom = PreviewZoom.Details;
        return true;
    }
    public void ExitComponent()
    {
        if (SelectedMemberId is null) return;
        SelectedMemberId = null;
        Zoom = _beforeMembers;
    }
    public void ChangeZoom(int delta)
    {
        Zoom = (PreviewZoom)Math.Clamp((int)Zoom + delta, 0, 2);
        if (Zoom != PreviewZoom.Details) SelectedMemberId = null;
    }
    public void ResetZoom() { SelectedMemberId = null; Zoom = PreviewZoom.Standard; }
    public ChatPreviewState ChatFor(Guid id)
    {
        if (!Nodes.Any(node => node.Id == id || node.Members.Any(member => member.Id == id)))
            throw new ArgumentException("Unknown preview component.", nameof(id));
        if (!_chats.TryGetValue(id, out ChatPreviewState? chat)) _chats.Add(id, chat = new());
        return chat;
    }

    public void EnsureVisible(PanelBounds node, int viewportWidth, int viewportHeight, int sceneWidth, int sceneHeight, int minX = 0, int minY = 0)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0) return;
        int x = ScrollX, y = ScrollY;
        if (node.Width > viewportWidth || node.X < x) x = node.X;
        else if (node.X + node.Width > x + viewportWidth) x = node.X + node.Width - viewportWidth;
        if (node.Height > viewportHeight || node.Y < y) y = node.Y;
        else if (node.Y + node.Height > y + viewportHeight) y = node.Y + node.Height - viewportHeight;
        ScrollX = Math.Clamp(x, minX, Math.Max(minX, sceneWidth - viewportWidth));
        ScrollY = Math.Clamp(y, minY, Math.Max(minY, sceneHeight - viewportHeight));
    }
}

public sealed class PreviewGraphLayout
{
    public int CardWidth { get; }
    public int CardHeight { get; }
    public int Gap { get; }
    public int Width => CardWidth * 3 + Gap * 2 + 4;
    public int Height => CardHeight * 3 + 6 + 2;
    public PreviewGraphLayout(PreviewZoom zoom, int viewportWidth)
    {
        CardWidth = Math.Min(zoom switch { PreviewZoom.Overview => 16, PreviewZoom.Details => 30, _ => 22 }, Math.Max(12, viewportWidth - 2));
        CardHeight = zoom switch { PreviewZoom.Overview => 3, PreviewZoom.Details => 7, _ => 5 };
        Gap = zoom == PreviewZoom.Overview ? 5 : 7;
    }
    public PanelBounds Bounds(PreviewGraphNode node) => new(2 + node.Column * (CardWidth + Gap),
        1 + node.Row * (CardHeight + 3), CardWidth, CardHeight);
}
