namespace Oryn.App;

public enum WorkspaceCommand
{
    NextPanel, PreviousPanel, MoveUp, MoveDown, Collapse, ChatTab, CodeTab, VisualizerTab,
    ShowVisualizer, ToggleVisualizerDock, DockVisualizerLeft, DockVisualizerRight, ResetPanelSizes, Quit
}
public sealed record ExplorerRow(FileEntry Entry, int Depth);
public sealed record SelectionResult(FileEntry Entry, DirectoryResult? Directory, FilePreview? Preview);

public sealed partial class WorkspaceState(string projectRoot)
{
    public string ProjectRoot { get; } = projectRoot;
    public WorkspacePanel Focus { get; private set; } = WorkspacePanel.Explorer;
    public WorkspaceTab ActiveTab { get; private set; } = WorkspaceTab.Chat;
    public IReadOnlyList<ExplorerRow> Rows => _rows;
    public int SelectedIndex { get; private set; }
    public ExplorerRow? Selected => _rows.Count == 0 ? null : _rows[SelectedIndex];
    public HashSet<string> ExpandedDirectories { get; } = new(StringComparer.Ordinal);
    public FilePreview? Preview { get; private set; }
    public string Status { get; private set; } = "M0 · local workspace";
    public bool QuitRequested { get; private set; }
    private readonly List<ExplorerRow> _rows = [];

    public void SetFocus(WorkspacePanel panel)
    {
        if (panel == WorkspacePanel.Visualizer) ShowVisualizer();
        else Focus = panel;
    }

    public void Select(int index)
    {
        SelectedIndex = _rows.Count == 0 ? 0 : Math.Clamp(index, 0, _rows.Count - 1);
    }

    public void Dispatch(WorkspaceCommand command)
    {
        switch (command)
        {
            case WorkspaceCommand.NextPanel: CycleFocus(1); break;
            case WorkspaceCommand.PreviousPanel: CycleFocus(-1); break;
            case WorkspaceCommand.MoveUp: if (Focus == WorkspacePanel.Explorer) Select(SelectedIndex - 1); break;
            case WorkspaceCommand.MoveDown: if (Focus == WorkspacePanel.Explorer) Select(SelectedIndex + 1); break;
            case WorkspaceCommand.Collapse: if (Focus == WorkspacePanel.Explorer) CollapseSelected(); break;
            case WorkspaceCommand.ChatTab: ActiveTab = _contentTab = WorkspaceTab.Chat; Focus = WorkspacePanel.Center; break;
            case WorkspaceCommand.CodeTab: ActiveTab = _contentTab = WorkspaceTab.Code; Focus = WorkspacePanel.Center; break;
            case WorkspaceCommand.VisualizerTab: SetVisualizerPlacement(VisualizerPlacement.Tab); break;
            case WorkspaceCommand.ShowVisualizer: ShowVisualizer(); break;
            case WorkspaceCommand.ToggleVisualizerDock: ToggleVisualizerDock(); break;
            case WorkspaceCommand.DockVisualizerLeft: SetVisualizerPlacement(VisualizerPlacement.Left); break;
            case WorkspaceCommand.DockVisualizerRight: SetVisualizerPlacement(VisualizerPlacement.Right); break;
            case WorkspaceCommand.ResetPanelSizes: ResetPanelSizes(); break;
            case WorkspaceCommand.Quit: QuitRequested = true; break;
        }
    }

    public void LoadRoot(DirectoryResult result)
    {
        _rows.Clear();
        ExpandedDirectories.Clear();
        _rows.AddRange(result.Entries.Select(entry => new ExplorerRow(entry, 0)));
        Select(0);
        SetDirectoryStatus(result);
    }

    public bool ToggleExpandedSelection()
    {
        if (Selected is not { Entry.IsDirectory: true } row || !ExpandedDirectories.Contains(row.Entry.Path)) return false;
        CollapseAt(SelectedIndex);
        return true;
    }

    public async Task<SelectionResult?> LoadSelectionAsync(IWorkspaceFiles files, CancellationToken token)
    {
        if (Selected is not { } row) return null;
        if (row.Entry.IsLink) return new(row.Entry, null, new(row.Entry.Path, "", false, "Symbolic links are not followed in M0."));
        if (row.Entry.IsDirectory) return new(row.Entry, await files.ListAsync(row.Entry.Path, token), null);
        return new(row.Entry, null, await files.ReadAsync(row.Entry.Path, token));
    }

    // Apply results on the UI thread. Filesystem work is performed outside the renderer.
    public void Apply(SelectionResult result)
    {
        if (result.Directory is { } directory)
        {
            int index = _rows.FindIndex(row => row.Entry.Path == result.Entry.Path);
            if (index < 0 || ExpandedDirectories.Contains(result.Entry.Path)) return;
            if (directory.Error is null)
            {
                ExpandedDirectories.Add(result.Entry.Path);
                _rows.InsertRange(index + 1, directory.Entries.Select(entry => new ExplorerRow(entry, _rows[index].Depth + 1)));
                Select(index);
            }
            SetDirectoryStatus(directory);
        }
        else if (result.Preview is { } preview)
        {
            if (result.Entry.IsDirectory || result.Entry.IsLink) { Status = preview.Error ?? "Preview unavailable."; return; }
            Preview = preview;
            ActiveTab = _contentTab = WorkspaceTab.Code;
            Focus = WorkspacePanel.Center;
            Status = preview.Error ?? (preview.Truncated ? "Preview truncated · read-only" : "Read-only preview");
        }
    }

    public void SetStatus(string status) => Status = status;

    private void SetDirectoryStatus(DirectoryResult result) => Status = result.Error ??
        (result.Truncated ? "Directory truncated: entry limit reached." : $"{result.Entries.Count} entries · local") +
        (result.Warning is null ? "" : " · " + result.Warning);

    private void CollapseSelected()
    {
        if (Selected is not { } row) return;
        if (ExpandedDirectories.Contains(row.Entry.Path)) { CollapseAt(SelectedIndex); return; }
        if (row.Depth == 0) return;
        for (int i = SelectedIndex - 1; i >= 0; i--)
            if (_rows[i].Depth < row.Depth) { Select(i); CollapseAt(i); return; }
    }

    private void CollapseAt(int index)
    {
        int depth = _rows[index].Depth;
        int end = index + 1;
        while (end < _rows.Count && _rows[end].Depth > depth)
        {
            ExpandedDirectories.Remove(_rows[end].Entry.Path);
            end++;
        }
        ExpandedDirectories.Remove(_rows[index].Entry.Path);
        _rows.RemoveRange(index + 1, end - index - 1);
        Select(index);
    }
}
