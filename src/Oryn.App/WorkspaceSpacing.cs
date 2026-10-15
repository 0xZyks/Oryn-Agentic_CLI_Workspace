namespace Oryn.App;

/// <summary>Logical-pixel chrome spacing shared by drawing and viewport calculations.</summary>
public readonly record struct WorkspaceSpacing(bool Dense)
{
    public int Gap => Dense ? 4 : GraphicalWorkspaceController.CellWidth;
    public int NavbarTop => GraphicalWorkspaceController.CellHeight + Gap;
    public int NavbarHeight => GraphicalWorkspaceController.CellHeight + (Dense ? 0 : 8);
    public int NavbarTextOffset => Gap + (Dense ? 0 : 4);
    public int PanelTopOffset => NavbarTop + NavbarHeight + Gap - 2 * GraphicalWorkspaceController.CellHeight;
    public int ContentTopRows => Dense ? 2 : 3;
    public int ExplorerHeaderRows => Dense ? 4 : 5;
    public int SearchHeaderRows => ExplorerHeaderRows + 1;
    public int TreeOffset => 2 * Gap;
    public int ResultsOffset => 3 * Gap;
    public int TreeRows(int panelRows) => Math.Max(1,
        (panelRows * GraphicalWorkspaceController.CellHeight - Gap - ExplorerHeaderRows * GraphicalWorkspaceController.CellHeight - TreeOffset)
        / GraphicalWorkspaceController.CellHeight);
    public int SearchRows(int panelRows) => Math.Max(1,
        (panelRows * GraphicalWorkspaceController.CellHeight - Gap - SearchHeaderRows * GraphicalWorkspaceController.CellHeight - ResultsOffset)
        / (2 * GraphicalWorkspaceController.CellHeight));
}
