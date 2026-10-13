namespace Oryn.App;

public sealed partial class WorkspaceState
{
    public WorkspaceLayoutOptions LayoutOptions { get; private set; } = new();
    public WorkspaceSplitter? ResizingSplitter => _resize?.Splitter;
    private VisualizerPlacement _lastDock = VisualizerPlacement.Right;
    private WorkspaceTab _contentTab = WorkspaceTab.Chat;
    private ResizeSession? _resize;

    private sealed record ResizeSession(WorkspaceSplitter Splitter, int StartX, int StartWidth,
        VisualizerPlacement Placement, WorkspaceLayoutOptions OriginalOptions);

    public void ShowVisualizer()
    {
        if (LayoutOptions.VisualizerPlacement == VisualizerPlacement.Tab)
        {
            ActiveTab = WorkspaceTab.Visualizer;
            Focus = WorkspacePanel.Center;
        }
        else Focus = WorkspacePanel.Visualizer;
    }

    public void SetVisualizerPlacement(VisualizerPlacement placement)
    {
        EndResize();
        LayoutOptions = LayoutOptions with { VisualizerPlacement = placement };
        if (placement == VisualizerPlacement.Tab)
        {
            ActiveTab = WorkspaceTab.Visualizer;
            Focus = WorkspacePanel.Center;
            Status = "Visualizer in its own tab";
        }
        else
        {
            _lastDock = placement;
            if (ActiveTab == WorkspaceTab.Visualizer) ActiveTab = _contentTab;
            Focus = WorkspacePanel.Visualizer;
            Status = $"Visualizer docked {placement.ToString().ToLowerInvariant()}";
        }
    }

    public void ToggleVisualizerDock() => SetVisualizerPlacement(
        LayoutOptions.VisualizerPlacement == VisualizerPlacement.Tab ? _lastDock : VisualizerPlacement.Tab);

    private void CycleFocus(int direction)
    {
        WorkspacePanel[] panels = LayoutOptions.VisualizerPlacement == VisualizerPlacement.Tab
            ? [WorkspacePanel.Explorer, WorkspacePanel.Center]
            : LayoutOptions.VisualizerPlacement == VisualizerPlacement.Left
                ? [WorkspacePanel.Explorer, WorkspacePanel.Visualizer, WorkspacePanel.Center]
                : [WorkspacePanel.Explorer, WorkspacePanel.Center, WorkspacePanel.Visualizer];
        int index = Array.IndexOf(panels, Focus);
        Focus = panels[(Math.Max(0, index) + direction + panels.Length) % panels.Length];
    }

    public bool BeginResize(int x, int y, int terminalWidth, int terminalHeight)
    {
        WorkspaceLayout layout = WorkspaceLayout.Calculate(terminalWidth, terminalHeight, Focus, LayoutOptions);
        if (layout.HitTestSplitter(x, y) is not { } splitter) return false;
        _resize = new(splitter, x, splitter == WorkspaceSplitter.Explorer ? layout.Explorer.Width : layout.Visualizer.Width,
            LayoutOptions.VisualizerPlacement, LayoutOptions);
        Status = "Drag to resize · Esc to cancel";
        return true;
    }

    public void UpdateResize(int x, int terminalWidth, int terminalHeight)
    {
        if (_resize is not { } resize) return;
        int direction = resize.Splitter == WorkspaceSplitter.Visualizer && resize.Placement == VisualizerPlacement.Right ? -1 : 1;
        SetPanelWidth(resize.Splitter, resize.StartWidth + (x - resize.StartX) * direction, terminalWidth, terminalHeight);
    }

    public void EndResize(bool cancel = false)
    {
        if (_resize is null) return;
        if (cancel) LayoutOptions = _resize.OriginalOptions;
        _resize = null;
        if (cancel) Status = "Resize cancelled";
    }

    public void ResizeFocusedPanel(int delta, int terminalWidth, int terminalHeight)
    {
        EndResize();
        WorkspaceLayout layout = WorkspaceLayout.Calculate(terminalWidth, terminalHeight, Focus, LayoutOptions);
        if (layout.Compact) { Status = "Widen the terminal to resize side panels"; return; }
        if (Focus == WorkspacePanel.Explorer)
            SetPanelWidth(WorkspaceSplitter.Explorer, layout.Explorer.Width + delta, terminalWidth, terminalHeight);
        else if (Focus == WorkspacePanel.Visualizer)
            SetPanelWidth(WorkspaceSplitter.Visualizer, layout.Visualizer.Width + delta, terminalWidth, terminalHeight);
        else
        {
            SetPanelWidth(WorkspaceSplitter.Explorer, layout.Explorer.Width - delta, terminalWidth, terminalHeight);
            WorkspaceLayout resized = WorkspaceLayout.Calculate(terminalWidth, terminalHeight, Focus, LayoutOptions);
            int remaining = delta - (resized.Center.Width - layout.Center.Width);
            if (remaining != 0 && LayoutOptions.VisualizerPlacement != VisualizerPlacement.Tab)
                SetPanelWidth(WorkspaceSplitter.Visualizer, resized.Visualizer.Width - remaining, terminalWidth, terminalHeight);
        }
    }

    public void ResetPanelSizes()
    {
        EndResize();
        LayoutOptions = LayoutOptions with { ExplorerWidth = null, VisualizerWidth = null };
        Status = "Default panel sizes restored";
    }

    private void SetPanelWidth(WorkspaceSplitter splitter, int requested, int terminalWidth, int terminalHeight)
    {
        WorkspaceLayout layout = WorkspaceLayout.Calculate(terminalWidth, terminalHeight, Focus, LayoutOptions);
        if (layout.Compact) return;
        if (splitter == WorkspaceSplitter.Explorer)
            LayoutOptions = LayoutOptions with { ExplorerWidth = Math.Clamp(requested, WorkspaceLayout.MinExplorerWidth,
                layout.Explorer.Width + layout.Center.Width - WorkspaceLayout.MinCenterWidth) };
        else if (LayoutOptions.VisualizerPlacement != VisualizerPlacement.Tab)
            LayoutOptions = LayoutOptions with { VisualizerWidth = Math.Clamp(requested, WorkspaceLayout.MinVisualizerWidth,
                layout.Visualizer.Width + layout.Center.Width - WorkspaceLayout.MinCenterWidth) };
        WorkspaceLayout resized = WorkspaceLayout.Calculate(terminalWidth, terminalHeight, Focus, LayoutOptions);
        Status = $"Files {resized.Explorer.Width} · Center {resized.Center.Width}" +
            (LayoutOptions.VisualizerPlacement == VisualizerPlacement.Tab ? "" : $" · Visualizer {resized.Visualizer.Width}");
    }
}
