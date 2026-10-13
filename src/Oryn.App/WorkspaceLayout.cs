namespace Oryn.App;

public enum WorkspacePanel { Explorer, Center, Visualizer }
public enum WorkspaceTab { Chat, Code, Visualizer }
public enum VisualizerPlacement { Tab, Left, Right }
public enum WorkspaceSplitter { Explorer, Visualizer }

public sealed record WorkspaceLayoutOptions
{
    public VisualizerPlacement VisualizerPlacement { get; init; } = VisualizerPlacement.Right;
    public int? ExplorerWidth { get; init; }
    public int? VisualizerWidth { get; init; }
}

public readonly record struct PanelBounds(int X, int Y, int Width, int Height)
{
    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
}

public sealed record WorkspaceLayout(PanelBounds Explorer, PanelBounds Center, PanelBounds Visualizer,
    bool Compact, WorkspacePanel VisiblePanel)
{
    public const int MinExplorerWidth = 18;
    public const int MinVisualizerWidth = 20;
    public const int MinCenterWidth = 36;
    public PanelBounds ExplorerSplitter { get; init; }
    public PanelBounds VisualizerSplitter { get; init; }

    public static WorkspaceLayout Calculate(int width, int height, WorkspacePanel focus, WorkspaceLayoutOptions? options = null)
    {
        options ??= new();
        width = Math.Max(0, width);
        int top = height >= 12 ? 4 : 1;
        int bottom = height >= 12 ? 3 : 1;
        int bodyHeight = Math.Max(0, height - top - bottom);
        int inset = width >= 12 ? 2 : 0;
        bool docked = options.VisualizerPlacement != VisualizerPlacement.Tab;
        if (!docked && focus == WorkspacePanel.Visualizer) focus = WorkspacePanel.Center;
        if (width < 90 || height < 12)
        {
            PanelBounds visible = new(inset, top, Math.Max(0, width - inset * 2), bodyHeight);
            return new(focus == WorkspacePanel.Explorer ? visible : default,
                focus == WorkspacePanel.Center ? visible : default,
                focus == WorkspacePanel.Visualizer ? visible : default, true, focus);
        }

        const int gap = 2;
        int available = width - inset * 2 - (docked ? gap * 2 : gap);
        int explorer = Math.Clamp(options.ExplorerWidth ?? Math.Clamp(width / 5, 20, 30),
            MinExplorerWidth, available - MinCenterWidth - (docked ? MinVisualizerWidth : 0));
        int visualizer = docked ? Math.Clamp(options.VisualizerWidth ?? Math.Clamp(width / 4, 24, 34),
            MinVisualizerWidth, available - explorer - MinCenterWidth) : 0;
        int center = available - explorer - visualizer;
        PanelBounds files = new(inset, top, explorer, bodyHeight);
        PanelBounds explorerSplitter = new(inset + explorer, top, gap, bodyHeight);
        int nextX = explorerSplitter.X + gap;
        if (!docked)
            return new(files, new(nextX, top, center, bodyHeight), default, false, focus)
                { ExplorerSplitter = explorerSplitter };
        if (options.VisualizerPlacement == VisualizerPlacement.Left)
            return new(files, new(nextX + visualizer + gap, top, center, bodyHeight),
                new(nextX, top, visualizer, bodyHeight), false, focus)
                { ExplorerSplitter = explorerSplitter, VisualizerSplitter = new(nextX + visualizer, top, gap, bodyHeight) };
        return new(files, new(nextX, top, center, bodyHeight),
            new(nextX + center + gap, top, visualizer, bodyHeight), false, focus)
            { ExplorerSplitter = explorerSplitter, VisualizerSplitter = new(nextX + center, top, gap, bodyHeight) };
    }

    public WorkspacePanel? HitTest(int x, int y)
    {
        if (Explorer.Contains(x, y)) return WorkspacePanel.Explorer;
        if (Center.Contains(x, y)) return WorkspacePanel.Center;
        if (Visualizer.Contains(x, y)) return WorkspacePanel.Visualizer;
        return null;
    }

    public WorkspaceSplitter? HitTestSplitter(int x, int y)
    {
        if (ExplorerSplitter.Contains(x, y)) return WorkspaceSplitter.Explorer;
        if (VisualizerSplitter.Contains(x, y)) return WorkspaceSplitter.Visualizer;
        return null;
    }
}
