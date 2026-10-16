using System.Globalization;
using Oryn.App;
using SkiaSharp;

namespace Oryn.Ui.Sdl;

/// <summary>Skia drawing and hit regions; application state lives in Oryn.App.</summary>
public sealed partial class WorkspacePainter : IDisposable
{
    private const int CW = GraphicalWorkspaceController.CellWidth, CH = GraphicalWorkspaceController.CellHeight;
    private const int HeaderExtra = 8;
    private WorkspaceSpacing Spacing => _ui.Spacing;
    private readonly SKTypeface _typeface;
    private readonly SKFont _font;
    private readonly SKFont _uiFont;
    private readonly SKFont _smallFont;
    private readonly SKFont _titleFont;
    private System.Numerics.Vector2 _pointer = new(-1, -1);
    private System.Numerics.Vector2 _sceneCenter;
    private (int Width, int Height, PreviewZoom Zoom) _sceneFrame;
    private readonly SKPaint _paint = new() { IsAntialias = true };
    private SKCanvas _canvas = null!;
    private long _animationTime;
    private readonly List<(SKRect Bounds, Action<float, float> Action)> _hits = [];
    private readonly List<(Guid Id, GraphBox Box, System.Numerics.Vector2 Origin)> _graphHits = [];
    private readonly GraphicalWorkspaceController _ui;
    private readonly bool _color;
    private readonly bool _unicode;
    private SKColor Background => _color ? ThemeBase : SKColor.Parse("#101010");
    private SKColor Active => !_color ? SKColor.Parse("#252525") : DefaultTheme ? SKColor.Parse("#142130") : Blend(ThemeBase, ThemeInk, .10f);
    private SKColor Inactive => !_color ? SKColor.Parse("#121212") : DefaultTheme ? SKColor.Parse("#0A111A") : Blend(ThemeBase, ThemeInk, .025f);
    private SKColor TextColor => _color ? Readable(ThemeInk, Active) : SKColor.Parse("#D2DAE6");
    private SKColor Muted => _color ? Readable(DefaultTheme ? SKColor.Parse("#899BB1") : Blend(ThemeBase, ThemeInk, .55f), Active) : SKColor.Parse("#899BB1");
    private SKColor Accent => _color ? Readable(Color(_ui.DisplayAppearance.AccentColor), Active, 3) : SKColors.White;
    private SKColor Selection => _color ? Blend(Active, Accent, .16f) : SKColor.Parse("#454545");
    private SKColor Border => _color ? DefaultTheme ? SKColor.Parse("#26394D") : Blend(ThemeBase, ThemeInk, .25f) : SKColor.Parse("#454545");
    private SKColor Raised => _color ? DefaultTheme ? SKColor.Parse("#131E2B") : Blend(ThemeBase, ThemeInk, .08f) : SKColor.Parse("#272727");
    public WorkspacePainter(GraphicalWorkspaceController ui, bool color = true, bool unicode = true)
    {
        _ui = ui; _color = color; _unicode = unicode;
        string[] preferred = ["JetBrains Mono", "Noto Sans Mono", "DejaVu Sans Mono", "Menlo", "Consolas", "monospace"];
        string[] families = SKFontManager.Default.FontFamilies.ToArray();
        string family = preferred.FirstOrDefault(p => families.Contains(p, StringComparer.OrdinalIgnoreCase)) ?? "monospace";
        _typeface = SKTypeface.FromFamilyName(family); _font = new(_typeface, 15) { Edging = SKFontEdging.SubpixelAntialias };
        _uiFont = new(_typeface, 14) { Edging = SKFontEdging.Antialias };
        _smallFont = new(_typeface, 12) { Edging = SKFontEdging.Antialias };
        _titleFont = new(_typeface, 15) { Edging = SKFontEdging.Antialias };
    }
    public void Draw(SKCanvas canvas, int width, int height, float scaleX = 1, float scaleY = 1, long? animationTime = null)
    {
        _animationTime = animationTime ?? GraphicalWorkspaceController.Now;
        _canvas = canvas; _hits.Clear(); _graphHits.Clear(); _chatGeometry = null;
        canvas.Clear(Background); canvas.Save(); canvas.Scale(scaleX, scaleY);
        _ui.Resize(width, height);
        int columns = _ui.Columns, rows = _ui.Rows;
        Rect(0, 0, columns * CW, Spacing.NavbarTop + Spacing.NavbarHeight, Inactive);
        _canvas.Save(); _canvas.Translate(0, 4);
        Star(21, 12, 5, Accent);
        UiText("ORYN", 4, 0, TextColor, 8, title: true);
        UiText("/ " + Path.GetFileName(_ui.State.ProjectRoot), 11, 0, Muted, Math.Max(1, columns - 23));
        Circle((columns - 8) * CW, 11, 2, StatusColor(PreviewDesignStatus.Validated));
        UiText("LOCAL", Math.Max(2, columns - 7), 0, Muted, 6, small: true);
        _canvas.Restore();
        var menus = GraphicalWorkspaceController.NavbarLabels;
        int selectedEnd = 2 + menus.Take(_ui.NavbarIndex + 1).Sum(menu => menu.Length + 3);
        int mx = 2 - (_ui.NavbarFocused || _ui.OpenNavbarIndex is not null ? Math.Max(0, selectedEnd - columns + 1) : 0);
        _canvas.Save(); _canvas.ClipRect(SKRect.Create(0, Spacing.NavbarTop, columns * CW, Spacing.NavbarHeight));
        for (int i = 0; i < menus.Count; i++)
        {
            string menu = menus[i]; int index = i;
            bool selected = (_ui.NavbarFocused && _ui.NavbarIndex == i) || _ui.OpenNavbarIndex == i;
            SKRect menuPixels = SKRect.Create((mx - 1) * CW, Spacing.NavbarTop, (menu.Length + 2) * CW, Spacing.NavbarHeight);
            if (selected || menuPixels.Contains(_pointer.X, _pointer.Y)) RoundedPixels(menuPixels, selected ? Selection : Raised, 2);
            if (selected) Rect(mx * CW, Spacing.NavbarTop + Spacing.NavbarHeight - 2, menu.Length * CW, 2, Accent);
            _canvas.Save(); _canvas.Translate(0, Spacing.NavbarTextOffset);
            UiText(menu, mx, 1, selected ? Accent : Muted); _canvas.Restore();
            _hits.Add((menuPixels, (_, _) => _ui.OpenNavbarMenu(index)));
            mx += menu.Length + 3;
        }
        _canvas.Restore();
        WorkspaceLayout layout = _ui.Layout;
        Panel(layout.Explorer, WorkspacePanel.Explorer, "Sources", DrawExplorer);
        Panel(layout.Center, WorkspacePanel.Center, "", DrawCenter);
        Panel(layout.Visualizer, WorkspacePanel.Visualizer, "Visualiser", b => DrawGraph(new(b.X, b.Y + Spacing.ContentTopRows - 1, b.Width, Math.Max(0, b.Height - Spacing.ContentTopRows + 1)), Spacing.Gap / 2));
        Splitter(layout.ExplorerSplitter); Splitter(layout.VisualizerSplitter);
        if (rows >= 3)
        {
            Fill(new(0, rows - 2, columns, 2), Background);
            LineSegment(CW, (rows - 2) * CH, (columns - 1) * CW, (rows - 2) * CH, Border);
            UiText((_ui.Loading ? "loading / " : "") + _ui.State.Status, 1, rows - 2, Muted, columns - 2, small: true);
            string scope = _ui.State.FocusArea == PanelFocusArea.Tabs ? "Tabs"
                : _ui.State.Focus == WorkspacePanel.Explorer ? "Files"
                : _ui.GraphActive ? _ui.State.Graph.SelectedMemberId is null ? "Graph cards" : "Graph members"
                : _ui.State.ActiveTab == WorkspaceTab.Chat ? _ui.HistoryFocused ? "Chat history" : "Chat input" : "Code";
            string hint = _ui.Keyboard.LeaderPending ? "Super leader pending / Esc cancel"
                : _ui.Overlay == WorkspaceOverlay.Appearance ? "Appearance / Tab accent-theme / Alt+A apply / Alt+S save / Esc cancel"
                : _ui.SearchFocused ? "Search / file name or text / Enter results / Esc clear"
                : _ui.SearchActive && _ui.State.Focus == WorkspacePanel.Explorer ? "Search results / Up-Down select / Enter open / Tab edit / Esc clear"
                : _ui.Overlay == WorkspaceOverlay.Keybindings ? "Keybindings / Tab fields / Alt+A apply / Alt+S save / Esc cancel"
                : _ui.HistoryFocused && (_ui.Overlay == WorkspaceOverlay.ElementChat || _ui.Overlay == WorkspaceOverlay.None && _ui.State.ActiveTab == WorkspaceTab.Chat)
                    ? "Chat history / drag select / Ctrl+C copy / Ctrl+A all / Tab input"
                : _ui.Overlay == WorkspaceOverlay.ElementChat ? "Component chat / Enter send / Shift+Enter newline / Esc close"
                : _ui.Overlay != WorkspaceOverlay.None ? "Dialog / arrows select / Enter confirm / Esc close"
                : _ui.NavbarFocused ? "Menu bar / arrows or Tab select / Enter open / Shift+Tab or Esc return"
                : _ui.State.FocusArea == PanelFocusArea.Tabs ? "Tabs / arrows select / Enter content / Esc content"
                : $"{scope} / {_ui.Keyboard.DisplayHint(KeyboardAction.NextPanel).Split(" / ")[0]} panels / {_ui.Keyboard.DisplayHint(KeyboardAction.ToggleTabFocus).Split(" / ")[^1]} tabs";
            UiText(hint, 1, rows - 1, Muted, columns - 2, small: true);
        }
        if (_ui.Overlay != WorkspaceOverlay.None) DrawOverlay();
        canvas.Restore();
        _ui.Dirty = false;
    }
    private void Panel(PanelBounds b, WorkspacePanel panel, string title, Action<PanelBounds> draw)
    {
        if (b.Width <= 0 || b.Height <= 0) return;
        bool focused = !_ui.NavbarFocused && _ui.Overlay == WorkspaceOverlay.None && _ui.State.Focus == panel;
        SKRect panelPixels = Pixels(b);
        panelPixels.Top += Spacing.PanelTopOffset; panelPixels.Bottom -= Spacing.Gap;
        if (panelPixels.Height <= 0) return;
        RoundedPixels(panelPixels, focused ? Active : Inactive, 2);
        int headerHeight = CH + HeaderExtra + Spacing.Gap;
        Rect(panelPixels.Left, panelPixels.Top, panelPixels.Width, headerHeight, focused ? Raised : Inactive);
        _paint.Color = Border; _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1;
        _canvas.DrawRoundRect(panelPixels, 2, 2, _paint); _paint.Style = SKPaintStyle.Fill;
        LineSegment(panelPixels.Left, panelPixels.Top + headerHeight, panelPixels.Right, panelPixels.Top + headerHeight, Border);
        _hits.Add((panelPixels, (_, _) => _ui.FocusPanel(panel)));
        _canvas.Save(); _canvas.ClipRect(panelPixels);
        if (title.Length > 0)
        {
            _canvas.Save(); _canvas.Translate(0, HeaderExtra / 2f + Spacing.PanelTopOffset + Spacing.Gap / 2f);
            UiText(title, b.X + 1, b.Y, focused ? TextColor : Muted, b.Width - 2, title: true);
            _canvas.Restore();
        }
        draw(b);
        if (!focused) RoundedPixels(panelPixels, new SKColor(0, 0, 0, 36), 2);
        _canvas.Restore();
        if (focused) Rect(panelPixels.Left, panelPixels.Top, panelPixels.Width, 2, Accent);
    }
    private void Splitter(PanelBounds b)
    {
        if (b.Width == 0) return;
        if (Hovered(b) || _ui.State.ResizingSplitter is not null)
            Rect((b.X + b.Width / 2f) * CW, b.Y * CH + Spacing.PanelTopOffset, 1, Math.Max(0, b.Height * CH - Spacing.PanelTopOffset - Spacing.Gap), Accent.WithAlpha(150));
        SKRect splitter = Pixels(b); splitter.Top += Spacing.PanelTopOffset; splitter.Bottom -= Spacing.Gap;
        _hits.Add((splitter, (x, y) => _ui.State.BeginResize((int)(x / CW), (int)(y / CH), _ui.Columns, _ui.Rows)));
    }
    private void DrawExplorer(PanelBounds b)
    {
        _canvas.Save(); _canvas.Translate(0, Spacing.Gap / 2f);
        Text(Path.GetFileName(_ui.State.ProjectRoot) + "/", b.X + 1, b.Y + Spacing.ContentTopRows, Muted, b.Width - 2);
        _canvas.Restore();
        DrawSearchField(b);
        if (_ui.SearchActive) { DrawSearchResults(b); return; }
        int count = Spacing.TreeRows(b.Height);
        _canvas.Save(); _canvas.Translate(0, Spacing.TreeOffset);
        for (int i = 0; i < count && i + _ui.ExplorerTop < _ui.State.Rows.Count; i++)
        {
            int index = i + _ui.ExplorerTop;
            ExplorerRow row = _ui.State.Rows[index]; int y = b.Y + Spacing.ExplorerHeaderRows + i;
            bool selected = index == _ui.State.SelectedIndex;
            if (selected || Hovered(new(b.X, y, b.Width, 1), Spacing.TreeOffset)) Round(new(b.X + 1, y, b.Width - 2, 1), selected ? Selection : Raised, 4);
            if (selected) Rect(b.X * CW + 2, y * CH + 3, 2, CH - 6, Accent);
            int indent = Math.Min(row.Depth * 2, Math.Max(0, b.Width - 8));
            float iconX = (b.X + 2 + indent) * CW, iconY = y * CH + 11;
            if (row.Entry.IsDirectory)
            {
                bool expanded = _ui.State.ExpandedDirectories.Contains(row.Entry.Path);
                LineSegment(iconX - 3, iconY - 3, iconX + (expanded ? 0 : 3), iconY, Muted);
                LineSegment(iconX + (expanded ? 0 : 3), iconY, iconX + (expanded ? 3 : -3), iconY + (expanded ? -3 : 3), Muted);
            }
            else FileIcon(iconX, iconY, row.Entry.Name);
            Text(row.Entry.Name + (row.Entry.IsDirectory ? "/" : ""), b.X + 4 + indent, y,
                selected ? Accent : TextColor, b.Width - 5 - indent);
            Hit(new(b.X, y, b.Width, 1), (_, _) => _ui.SelectExplorer(index), offsetY: Spacing.TreeOffset);
        }
        _canvas.Restore();
    }
    private void DrawCenter(PanelBounds b)
    {
        WorkspaceTab[] tabs = [WorkspaceTab.Chat, WorkspaceTab.Code, WorkspaceTab.Visualizer]; int x = b.X + 1;
        foreach (WorkspaceTab tab in tabs)
        {
            string name = tab switch { WorkspaceTab.Visualizer => "Visualiser", WorkspaceTab.Chat => "Chat", _ => "Sources" };
            bool selected = _ui.State.ActiveTab == tab;
            PanelBounds tabBounds = new(x, b.Y, name.Length + 2, 1);
            SKRect tabPixels = Pixels(tabBounds); tabPixels.Offset(0, Spacing.PanelTopOffset + Spacing.Gap); tabPixels.Bottom += HeaderExtra - Spacing.Gap;
            if (selected || tabPixels.Contains(_pointer.X, _pointer.Y)) RoundedPixels(tabPixels, selected ? Selection : Raised, 2);
            _canvas.Save(); _canvas.Translate(0, HeaderExtra / 2f + Spacing.PanelTopOffset + Spacing.Gap / 2f);
            UiText(name, x + 1, b.Y, selected ? Accent : Muted);
            _canvas.Restore();
            if (selected && !_ui.NavbarFocused && _ui.Overlay == WorkspaceOverlay.None && _ui.State.FocusArea == PanelFocusArea.Tabs && _ui.State.Focus == WorkspacePanel.Center)
            {
                _paint.Color = Accent; _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1;
                tabPixels.Inflate(-.5f, -.5f); _canvas.DrawRect(tabPixels, _paint); _paint.Style = SKPaintStyle.Fill;
            }
            Hit(tabBounds, (_, _) => _ui.SelectTab(tab), offsetY: Spacing.PanelTopOffset + Spacing.Gap, heightExtra: HeaderExtra - Spacing.Gap); x += name.Length + 3;
        }
        PanelBounds content = new(b.X, b.Y + Spacing.ContentTopRows, b.Width, Math.Max(0, b.Height - Spacing.ContentTopRows));
        _canvas.Save(); _canvas.ClipRect(Pixels(content));
        switch (_ui.State.ActiveTab)
        {
            case WorkspaceTab.Chat: DrawChat(content, _ui.State.Chat, Spacing.Gap / 2f); break;
            case WorkspaceTab.Code: DrawCode(content); break;
            case WorkspaceTab.Visualizer: DrawGraph(content); break;
        }
        _canvas.Restore();
    }
    private void DrawCode(PanelBounds b)
    {
        _canvas.Save(); _canvas.Translate(0, Spacing.Gap / 2f);
        Text(_ui.State.Preview is { } preview ? Path.GetRelativePath(_ui.State.ProjectRoot, preview.Path) + " / read-only" : "Code / read-only", b.X + 1, b.Y, Muted, b.Width - 2);
        _canvas.Restore();
        PanelBounds surface = new(b.X + 1, b.Y + 2, Math.Max(0, b.Width - 2), Math.Max(0, b.Height - 3));
        Fill(surface, Background); _canvas.Save(); _canvas.ClipRect(Pixels(surface));
        string[] lines = (_ui.State.Preview?.Content ?? "Select a file in the explorer to open its preview.").Replace("\r", "").Split('\n');
        int top = Math.Min(_ui.CodeTop, Math.Max(0, lines.Length - Math.Max(1, surface.Height - 1)));
        for (int i = 0; i < surface.Height - 1 && i + top < lines.Length; i++)
        {
            if (_ui.CodeMatchLine == i + top + 1) Fill(new(surface.X, surface.Y + 1 + i, surface.Width, 1), Selection);
            Text((i + top + 1).ToString().PadLeft(4), surface.X + 1, surface.Y + 1 + i, Muted);
            string line = lines[i + top].Replace("\t", "    ");
            Text(_ui.CodeLeft >= line.Length ? "" : line[_ui.CodeLeft..], surface.X + 7, surface.Y + 1 + i, TextColor, surface.Width - 8);
        }
        _canvas.Restore();
    }
    private sealed record Line(string Text, int Start, int End);
    private static List<Line> Wrap(string text, int width)
    {
        width = Math.Max(1, width); List<Line> lines = []; string part = ""; int start = 0;
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);
        int count = 0;
        while (elements.MoveNext())
        {
            string value = elements.GetTextElement(); int index = elements.ElementIndex;
            if (value == "\n") { lines.Add(new(part, start, index)); part = ""; count = 0; start = index + 1; }
            else
            {
                if (count >= width) { lines.Add(new(part, start, index)); start = index; part = ""; count = 0; }
                part += value == "\t" ? " " : value; count++;
            }
        }
        lines.Add(new(part, start, text.Length)); return lines;
    }
    private void Circle(float x, float y, float radius, SKColor color)
    {
        _paint.Color = color; _paint.Style = SKPaintStyle.Fill;
        _canvas.DrawCircle(x, y, radius, _paint);
    }
    private void Star(float x, float y, float radius, SKColor color)
    {
        using SKPath path = new();
        for (int i = 0; i < 8; i++)
        {
            float angle = -MathF.PI / 2 + i * MathF.PI / 4;
            float r = i % 2 == 0 ? radius : radius * .28f;
            float px = x + MathF.Cos(angle) * r, py = y + MathF.Sin(angle) * r;
            if (i == 0) path.MoveTo(px, py); else path.LineTo(px, py);
        }
        path.Close(); _paint.Color = color; _paint.Style = SKPaintStyle.Fill; _canvas.DrawPath(path, _paint);
    }
    private void ThinkingSpinner(float x, float y)
    {
        int active = (int)(_animationTime / 200 % 4);
        for (int i = 0; i < 4; i++)
        {
            float angle = -MathF.PI / 2 + i * MathF.PI / 2;
            float px = x + MathF.Cos(angle) * 5, py = y + MathF.Sin(angle) * 5;
            if (i == active) Star(px, py, 2.8f, Accent);
            else Circle(px, py, 1.1f, Muted);
        }
    }
    private void DrawEditor(PanelBounds input, TextEditState editor, List<Line> lines, int inner)
    {
        int cursorLine = lines.FindLastIndex(line => line.Start <= editor.Cursor);
        int first = Math.Max(0, cursorLine - Math.Max(1, input.Height - 2) + 1);
        Text(">", input.X + 1, input.Y + 1, Accent);
        _canvas.Save(); _canvas.ClipRect(Pixels(input));
        for (int i = first; i < lines.Count && i < first + input.Height - 2; i++)
        {
            Line line = lines[i]; int row = input.Y + 1 + i - first;
            int a = Math.Clamp(Math.Min(editor.Cursor, editor.Anchor) - line.Start, 0, line.Text.Length);
            int z = Math.Clamp(Math.Max(editor.Cursor, editor.Anchor) - line.Start, 0, line.Text.Length);
            if (z > a) Fill(new(input.X + 3 + new StringInfo(line.Text[..a]).LengthInTextElements, row,
                Math.Max(1, new StringInfo(line.Text[a..z]).LengthInTextElements), 1), Muted.WithAlpha(90));
            Text(line.Text, input.X + 3, row, TextColor, inner);
            if (i == cursorLine && _ui.AcceptsText && _ui.ActiveEditor == editor)
            {
                int column = new StringInfo(line.Text[..Math.Clamp(editor.Cursor - line.Start, 0, line.Text.Length)]).LengthInTextElements;
                Rect((input.X + 3 + column) * CW, row * CH + 3, 1.5f, CH - 6, Accent);
                if (_ui.Composition.Length > 0) Text(_ui.Composition, input.X + 3 + column, row, Accent, inner - column);
            }
        }
        _canvas.Restore();
    }
    private static void PlaceEditor(TextEditState editor, List<Line> lines, PanelBounds input, int x, int y)
    {
        int cursorLine = lines.FindLastIndex(line => line.Start <= editor.Cursor);
        int first = Math.Max(0, cursorLine - Math.Max(1, input.Height - 2) + 1);
        Line line = lines[Math.Clamp(first + y - input.Y - 1, 0, lines.Count - 1)];
        int[] starts = StringInfo.ParseCombiningCharacters(line.Text);
        int column = Math.Clamp(x - input.X - 3, 0, starts.Length);
        editor.Place(line.Start + (column < starts.Length ? starts[column] : line.Text.Length));
    }
    private void DrawGraph(PanelBounds b, int headingOffset = 0)
    {
        _canvas.Save(); _canvas.Translate(0, headingOffset);
        UiText("PROJECT MAP / " + _ui.State.Graph.Zoom + " / demo", b.X + 1, b.Y + 1, Muted, b.Width - 2, small: true);
        string[] controls = ["Tab", "Dock", "Left", "Right", "+", "-"]; int x = b.X + 1;
        KeyboardAction[] actions = [KeyboardAction.FocusVisualizer, KeyboardAction.ToggleDock, KeyboardAction.DockLeft, KeyboardAction.DockRight, KeyboardAction.GraphZoomIn, KeyboardAction.GraphZoomOut];
        for (int i = 0; i < controls.Length; i++)
        {
            int index = i; PanelBounds control = new(x, b.Y + 2, controls[i].Length + 1, 1);
            SKRect controlPixels = Pixels(control); controlPixels.Offset(0, headingOffset);
            Round(control, controlPixels.Contains(_pointer.X, _pointer.Y) ? Selection : Raised, 4);
            UiText(controls[i], x, b.Y + 2, Muted); Hit(new(x, b.Y + 2, controls[i].Length + 1, 1), (_, _) =>
            { if (index == 0) _ui.PlaceVisualizer(VisualizerPlacement.Tab); else _ui.Execute(actions[index], GraphicalWorkspaceController.Now); }, offsetY: headingOffset);
            x += controls[i].Length + 2;
        }
        _canvas.Restore();
        bool inspector = b.Width >= 92 && b.Height >= 18;
        PanelBounds rail = inspector ? new(b.X + b.Width - 28, b.Y + 4, 27, b.Height - 5) : default;
        PanelBounds view = new(b.X + 1, b.Y + 4, Math.Max(1, b.Width - 2 - (inspector ? 29 : 0)), Math.Max(1, b.Height - 5));
        Round(view, Background, 5);
        PreviewGraphLayout graph = new(_ui.State.Graph.Zoom, view.Width);
        GraphDragState placement = _ui.State.Graph.Placement;
        placement.Configure(_ui.State.Graph.Nodes, graph);
        GraphBox selectedBox = placement.Bounds(_ui.State.Graph.SelectedId);
        int sceneWidth = (int)Math.Ceiling(_ui.State.Graph.Nodes.Max(n => placement.Bounds(n.Id).Right) / CW) + 2;
        int sceneHeight = (int)Math.Ceiling(_ui.State.Graph.Nodes.Max(n => placement.Bounds(n.Id).Bottom) / CH) + 2;
        // Center on viewport/zoom changes only. Recentring every release would move
        // the entire canvas under the pointer after dragging an outer card.
        var frame = (view.Width, view.Height, _ui.State.Graph.Zoom);
        if (_sceneFrame != frame)
        {
            _sceneCenter = new(Math.Max(0, (view.Width - sceneWidth) / 2), Math.Max(0, (view.Height - sceneHeight) / 2));
            _sceneFrame = frame;
        }
        if (placement.DraggedId is null)
            _ui.State.Graph.EnsureVisible(new((int)(selectedBox.X / CW) + (int)_sceneCenter.X, (int)(selectedBox.Y / CH) + (int)_sceneCenter.Y, graph.CardWidth, graph.CardHeight),
                view.Width, view.Height, sceneWidth + (int)_sceneCenter.X, sceneHeight + (int)_sceneCenter.Y,
                Math.Min(0, (int)Math.Floor(_ui.State.Graph.Nodes.Min(n => placement.Bounds(n.Id).X) / CW) + (int)_sceneCenter.X - 2),
                Math.Min(0, (int)Math.Floor(_ui.State.Graph.Nodes.Min(n => placement.Bounds(n.Id).Y) / CH) + (int)_sceneCenter.Y - 2));
        int ox = view.X - _ui.State.Graph.ScrollX + (int)_sceneCenter.X, oy = view.Y - _ui.State.Graph.ScrollY + (int)_sceneCenter.Y;
        System.Numerics.Vector2 origin = new(ox * CW, oy * CH);
        _canvas.Save(); _canvas.ClipRect(Pixels(view));
        for (int gx = 0; gx < view.Width; gx += 3)
        for (int gy = 0; gy < view.Height; gy += 2)
            Circle((view.X + gx) * CW + 4, (view.Y + gy) * CH + 8, .7f, Border.WithAlpha(100));
        foreach (PreviewGraphLink link in _ui.State.Graph.Links)
        {
            GraphBox from = placement.Bounds(link.From), to = placement.Bounds(link.To);
            System.Numerics.Vector2 delta = to.Center - from.Center;
            System.Numerics.Vector2 Attach(GraphBox box, System.Numerics.Vector2 direction)
            {
                float factor = Math.Min(Math.Abs(direction.X) < .001f ? float.PositiveInfinity : box.Width / 2 / Math.Abs(direction.X),
                    Math.Abs(direction.Y) < .001f ? float.PositiveInfinity : box.Height / 2 / Math.Abs(direction.Y));
                return box.Center + direction * factor + origin;
            }
            var a = Attach(from, delta); var z = Attach(to, -delta);
            bool horizontal = Math.Abs(delta.X) >= Math.Abs(delta.Y);
            var tangent = horizontal ? new System.Numerics.Vector2(Math.Sign(delta.X), 0) : new System.Numerics.Vector2(0, Math.Sign(delta.Y));
            float bend = Math.Max(18, System.Numerics.Vector2.Distance(a, z) * .35f);
            var c1 = a + tangent * bend; var c2 = z - tangent * bend;
            SKColor connection = link.From == _ui.State.Graph.SelectedId || link.To == _ui.State.Graph.SelectedId ? Accent.WithAlpha(175) : Muted.WithAlpha(95);
            using SKPath path = new(); path.MoveTo(a.X, a.Y); path.CubicTo(c1.X, c1.Y, c2.X, c2.Y, z.X, z.Y);
            _paint.Color = connection; _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1.25f; _canvas.DrawPath(path, _paint); _paint.Style = SKPaintStyle.Fill;
            var perpendicular = new System.Numerics.Vector2(-tangent.Y, tangent.X); var arrow = z - tangent * 7;
            LineSegment(arrow.X + perpendicular.X * 3, arrow.Y + perpendicular.Y * 3, z.X, z.Y, connection);
            LineSegment(arrow.X - perpendicular.X * 3, arrow.Y - perpendicular.Y * 3, z.X, z.Y, connection);
            var middle = (a + c1 * 3 + c2 * 3 + z) / 8;
            float labelWidth = _smallFont.MeasureText(link.Label) + 12;
            RoundedPixels(SKRect.Create(middle.X - labelWidth / 2, middle.Y - 9, labelWidth, 17), Background, 4);
            UiPixels(link.Label, middle.X - labelWidth / 2 + 6, middle.Y + 4, Muted, _smallFont);

        }
        foreach (PreviewGraphNode node in _ui.State.Graph.Nodes)
        {
            GraphBox world = placement.Bounds(node.Id);
            float px = world.X + origin.X, py = world.Y + origin.Y;
            PanelBounds card = new((int)Math.Floor(px / CW), (int)Math.Floor(py / CH), graph.CardWidth, graph.CardHeight);
            _canvas.Save(); _canvas.Translate(px - card.X * CW, py - card.Y * CH);
            GraphBox screen = new(px, py, world.Width, world.Height);
            float left = Math.Max(screen.X, view.X * CW), top = Math.Max(screen.Y, view.Y * CH);
            float right = Math.Min(screen.Right, (view.X + view.Width) * CW), bottom = Math.Min(screen.Bottom, (view.Y + view.Height) * CH);
            if (right > left && bottom > top) _graphHits.Add((node.Id, new(left, top, right - left, bottom - top), origin));
            bool selected = node.Id == _ui.State.Graph.SelectedId;
            RoundedPixels(SKRect.Create(card.X * CW + 2, card.Y * CH + 4, card.Width * CW, card.Height * CH), new SKColor(0, 0, 0, 65), 7);
            Round(card, selected ? Selection : Raised, 7);
            Outline(card, selected ? Accent : Hovered(card) ? Muted : Border, 7);
            UiText(node.Name, card.X + 1, card.Y, TextColor, card.Width - 2, title: _ui.State.Graph.Zoom != PreviewZoom.Overview);
            Circle((card.X + 1) * CW + 3, (card.Y + 1) * CH + 11, 2.4f, StatusColor(node.Status));
            UiText(Status(node.Status), card.X + 2, card.Y + 1, StatusColor(node.Status), card.Width - 3, small: true);
            if (_ui.State.Graph.Zoom != PreviewZoom.Overview) UiText(node.Kind, card.X + 1, card.Y + 2, Muted, card.Width - 2, small: true);
            if (_ui.State.Graph.Zoom == PreviewZoom.Details) LineSegment((card.X + 1) * CW, (card.Y + 3) * CH + 3, (card.X + card.Width - 1) * CW, (card.Y + 3) * CH + 3, Border);
            PanelBounds clickable = Intersection(card, view);
            if (clickable.Width > 0 && clickable.Height > 0) Hit(clickable, (_, _) => { _ui.FocusPanel(_ui.State.LayoutOptions.VisualizerPlacement == VisualizerPlacement.Tab ? WorkspacePanel.Center : WorkspacePanel.Visualizer); _ui.State.Graph.Select(node.Id); });
            if (_ui.State.Graph.Zoom == PreviewZoom.Details)
                for (int i = 0; i < node.Members.Length; i++)
                {
                    PreviewGraphMember member = node.Members[i]; int y = card.Y + 4 + i;
                    bool active = member.Id == _ui.State.Graph.SelectedMemberId;
                    if (active || Hovered(new(card.X, y, card.Width, 1))) Round(new(card.X + 1, y, card.Width - 2, 1), active ? Selection : Inactive, 4);
                    Text((active ? "> " : "  ") + member.Name, card.X + 1, y, active ? Accent : TextColor, card.Width - 2);
                    PanelBounds memberHit = Intersection(new(card.X, y, card.Width, 1), view);
                    if (memberHit.Height > 0 && memberHit.Width > 0) Hit(memberHit, (_, _) => { _ui.FocusPanel(_ui.State.LayoutOptions.VisualizerPlacement == VisualizerPlacement.Tab ? WorkspacePanel.Center : WorkspacePanel.Visualizer); _ui.State.Graph.Select(node.Id); _ui.State.Graph.SelectMember(member.Id); });
                }
            _canvas.Restore();
        }
        _canvas.Restore();
        if (inspector) DrawInspector(rail);
    }
    private void DrawInspector(PanelBounds b)
    {
        Round(b, Inactive, 7); Outline(b, Border, 7);
        var target = _ui.State.Graph.Target;
        UiText("SELECTED ELEMENT", b.X + 2, b.Y + 1, Muted, b.Width - 4, small: true);
        UiText(target.Name, b.X + 2, b.Y + 3, TextColor, b.Width - 4, title: true);
        UiText(target.Kind, b.X + 2, b.Y + 4, Muted, b.Width - 4, small: true);
        Circle((b.X + 2) * CW + 3, (b.Y + 6) * CH + 11, 2.5f, StatusColor(target.Status));
        UiText(Status(target.Status), b.X + 3, b.Y + 6, StatusColor(target.Status), b.Width - 5);
        UiText("DEMO / M0 FIXTURE", b.X + 2, b.Y + 8, Muted, b.Width - 4, small: true);
        UiText("Identity", b.X + 2, b.Y + 10, Muted, b.Width - 4, small: true);
        Text(target.Id.ToString()[..18], b.X + 2, b.Y + 11, Muted, b.Width - 4);
        Text(target.Id.ToString()[18..], b.X + 2, b.Y + 12, Muted, b.Width - 4);
        int relations = _ui.State.Graph.Links.Count(l => l.From == _ui.State.Graph.SelectedId || l.To == _ui.State.Graph.SelectedId);
        if (b.Height > 17) UiText($"{relations} project relations", b.X + 2, b.Y + 13, Muted, b.Width - 4);
        if (b.Height < 20) return;
        string[] labels = ["Details", "Discussion"];
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i; PanelBounds button = new(b.X + 1, b.Y + b.Height - 5 + i * 2, b.Width - 2, 1);
            Round(button, Hovered(button) ? Selection : Raised, 5);
            UiText(labels[i], button.X + 1, button.Y, i == 0 ? TextColor : Accent, button.Width - 2);
            Hit(button, (_, _) => _ui.OpenOverlay(index == 0 ? WorkspaceOverlay.Details : WorkspaceOverlay.ElementChat));
        }
    }
    private static string Status(PreviewDesignStatus status) => status switch
    { PreviewDesignStatus.ToValidate => "À valider", PreviewDesignStatus.Validated => "Validé", PreviewDesignStatus.InProgress => "En cours", _ => "Implémenté" };
    private SKColor StatusColor(PreviewDesignStatus status) => !_color ? TextColor : Readable(SKColor.Parse(status switch
    { PreviewDesignStatus.ToValidate => "#a49cba", PreviewDesignStatus.Validated => "#88b8dc", PreviewDesignStatus.InProgress => "#debf87", _ => "#92bfa2" }), Active, 3);
    private void DrawOverlay()
    {
        _hits.Clear(); // Modal hit regions never leak through to underlying panels.
        Rect(0, 0, _ui.Columns * CW, _ui.Rows * CH, new SKColor(0, 0, 0, 170));
        if (_ui.Overlay != WorkspaceOverlay.Appearance) _pickerDrag = 0;
        int width = Math.Min(_ui.Columns - 2, _ui.Overlay is WorkspaceOverlay.ElementChat or WorkspaceOverlay.Keybindings ? 100 : 66);
        int height = Math.Min(_ui.Rows - 2, _ui.Overlay is WorkspaceOverlay.ElementChat or WorkspaceOverlay.Keybindings ? 30 : 15);
        if (_ui.Overlay == WorkspaceOverlay.Appearance) { width = Math.Min(_ui.Columns - 2, 72); height = Math.Min(_ui.Rows - 2, 24); }
        if (_ui.Overlay == WorkspaceOverlay.InferenceSetup) { width = Math.Min(_ui.Columns - 2, 100); height = Math.Min(_ui.Rows - 2, 30); }
        width = Math.Max(1, width); height = Math.Max(1, height);
        PanelBounds box = new((_ui.Columns - width) / 2, (_ui.Rows - height) / 2, width, height);
        if (_ui.Overlay == WorkspaceOverlay.Menu && _ui.OpenNavbarIndex is { } menuIndex)
        {
            width = Math.Min(_ui.Columns - 2, 38); height = Math.Min(_ui.Rows - 3, _ui.MenuItems.Count + 6);
            int menuX = 2 + GraphicalWorkspaceController.NavbarLabels.Take(menuIndex).Sum(label => label.Length + 3);
            box = new(Math.Clamp(menuX, 1, Math.Max(1, _ui.Columns - width - 1)),
                (Spacing.NavbarTop + Spacing.NavbarHeight + Spacing.Gap + CH - 1) / CH, Math.Max(1, width), Math.Max(1, height));
        }
        Round(box, Active, 2); Outline(box, Border, 2);
        Rect(box.X * CW, (box.Y + 1) * CH + 4, 2, CH - 8, Accent);
        _canvas.Save(); _canvas.ClipRect(Pixels(box));
        UiText(_ui.Overlay switch { WorkspaceOverlay.Menu => _ui.MenuTitle, WorkspaceOverlay.Options => "Component options", WorkspaceOverlay.Details => "Component / demo details", WorkspaceOverlay.ElementChat => "Component chat / demo", WorkspaceOverlay.Appearance => "Appearance", WorkspaceOverlay.InferenceSetup => "Inference / premier lancement", _ => "Keybindings" }, box.X + 2, box.Y + 1, Accent, box.Width - 9);
        UiText("Esc", box.X + box.Width - 5, box.Y + 1, Muted, 4, small: true);
        Hit(new(box.X + box.Width - 8, box.Y, 8, 2), (_, _) => _ui.CloseOverlay());
        if (_ui.Overlay is WorkspaceOverlay.Menu or WorkspaceOverlay.Options)
        {
            string[] items = _ui.Overlay == WorkspaceOverlay.Menu ? _ui.MenuItems.Select(item => item.Label).ToArray() : ["Show details", "Show chat (demo)", "Back"];
            for (int i = 0; i < items.Length; i++)
            {
                int index = i; int y = box.Y + 4 + i;
                if (i == _ui.OverlaySelection || Hovered(new(box.X + 1, y, box.Width - 2, 1))) Round(new(box.X + 1, y, box.Width - 2, 1), i == _ui.OverlaySelection ? Selection : Raised, 4);
                UiText((i == _ui.OverlaySelection ? "> " : "  ") + items[i], box.X + 2, y,
                    _ui.Overlay == WorkspaceOverlay.Menu && _ui.MenuItems[i].Action is null ? Muted : TextColor, box.Width - 4);
                Hit(new(box.X + 1, y, box.Width - 2, 1), (_, _) => _ui.ChooseOverlay(index, GraphicalWorkspaceController.Now));
            }
        }
        else if (_ui.Overlay == WorkspaceOverlay.Details && _ui.OverlayTarget is { } target)
        {
            string[] details = [target.Name, target.Kind + " / " + Status(target.Status), "UUID / " + target.Id, "Parent / " + (target.ParentId?.ToString() ?? "none"), "Origin / fictitious M0 fixture", "No analysis or real status validation."];
            for (int i = 0; i < details.Length; i++) Text(details[i], box.X + 2, box.Y + 4 + i, i == 0 ? Accent : TextColor, box.Width - 4);
        }
        else if (_ui.Overlay == WorkspaceOverlay.ElementChat)
            DrawChat(new(box.X + 1, box.Y + 3, box.Width - 2, box.Height - 4), _ui.CurrentChat);
        else if (_ui.Overlay == WorkspaceOverlay.Keybindings) DrawBindings(box);
        else if (_ui.Overlay == WorkspaceOverlay.Appearance) DrawAppearance(box);
        else if (_ui.Overlay == WorkspaceOverlay.InferenceSetup) DrawInferenceSetup(box);
        _canvas.Restore();
    }

    private void DrawInferenceSetup(PanelBounds b)
    {
        var setup = _ui.InferenceSetup;
        Text(_unicode ? "↑↓/Tab modèle · Échap annule / retour" : "Up/Down/Tab modele / Esc annule / retour", b.X + 2, b.Y + 2, Muted, b.Width - 4);
        int y = b.Y + 3, end = b.Y + b.Height - 5;
        void Row(string text, bool accent = false)
        { if (y < end) Text(text, b.X + 2, y++, accent ? Accent : TextColor, Math.Max(1, b.Width - 4)); }
        static string Size(long? bytes) => bytes is { } value ? $"{value / 1073741824.0:F1} Gio" : "inconnue";
        Row(_ui.BackendStatus, true);
        if (b.Height >= 22) Row("Config : " + _ui.ConfigurationPath);
        if (setup.Hardware is { } hardware)
        {
            Row($"RAM libre {Size(hardware.RamAvailableBytes)} / {Size(hardware.RamTotalBytes)} · CPU {hardware.LogicalProcessors}");
            if (b.Height >= 18) Row(hardware.Gpus.Count == 0 ? "GPU NVIDIA non détecté · CPU / Metal selon backend" : $"{hardware.Gpus[0].Name} · VRAM libre {Size(hardware.Gpus[0].AvailableBytes)}");
        }
        if (b.Height >= 22)
        {
            for (int i = 0; i < setup.Suggestions.Count; i++)
            {
                int index = i; var suggestion = setup.Suggestions[i]; int row = y;
                if (i == setup.Selection) Round(new(b.X + 1, row, b.Width - 2, 1), Selection, 2);
                Row((i == setup.Selection ? "> " : "  ") + suggestion.Model.FileName + (suggestion.Recommended ? " · suggéré" : ""));
                Hit(new(b.X + 1, row, b.Width - 2, 1), (_, _) => _ui.SelectSetupSuggestion(index));
            }
        }
        else if (setup.Selected is { } compact) Row("> " + compact.Model.FileName, true);
        if (setup.Selected is { } selected)
        {
            if (_ui.SelectedSetupProfile is { } profile && b.Height >= 22)
                Row($"Sélection : {selected.Slot} · ctx {profile.ContextTokens} · experts CPU {profile.CpuMoeLayers} · Flash {profile.FlashAttention}");
            if (b.Height >= 18)
            {
                Row(selected.Reason);
                Row($"Modèle {Size(selected.Model.Bytes)} · {selected.Model.License}");
                Row("Source : huggingface.co/" + selected.Model.Repository);
                string? key = InferenceInstallation.RuntimeKey(selected.Gpu);
                Row(key is null ? "Backend à configurer manuellement sur cet OS/architecture" : $"Backend {InferenceInstallation.Version} / {key} · {Size(InferenceInstallation.Archives(key).Sum(a => a.Bytes))}");
                Row("Backend : GitHub ggml-org/llama.cpp · licences dans l'archive · SHA256 vérifiés");
                Row("Aucun projet/chat envoyé · sélection puis Entrée pour appliquer");
            }
        }
        foreach (var line in Wrap(setup.Status, Math.Max(1, b.Width - 4))) Row(line.Text, true);
        int actionY = b.Y + b.Height - 4;
        string action = setup.Busy || _ui.ModelSwitching ? "En cours… Échap annule" : setup.ConfirmDownload ? "Entrée : confirmer le téléchargement"
            : "Entrée / clic : appliquer " + (setup.Selected?.Model.FileName ?? "la sélection");
        Text(action, b.X + 2, actionY, Accent, b.Width - 4);
        Hit(new(b.X + 1, actionY, b.Width - 2, 1), (_, _) => _ui.ActivateSetupSelection());
        int half = Math.Max(1, (b.Width - 4) / 2);
        Text("T : essai local", b.X + 2, actionY + 1, Muted, half);
        Text("N : continuer", b.X + 2 + half, actionY + 1, Muted, half);
        Hit(new(b.X + 2, actionY + 1, half, 1), (_, _) => _ui.ProbeSetupSelection());
        Hit(new(b.X + 2 + half, actionY + 1, half, 1), (_, _) => _ui.SkipInferenceSetup());
    }
    private void DrawBindings(PanelBounds b)
    {
        Text("Tab fields / Alt+A apply / Alt+S save / Esc cancel", b.X + 2, b.Y + 2, Muted, b.Width - 4);
        int available = Math.Max(1, b.Height - 12);
        int start = Math.Max(-1, _ui.BindingIndex - available + 1);
        for (int i = 0; i < available; i++)
        {
            int index = start + i; if (index >= KeyBindingSettings.Definitions.Count) break;
            string label = index < 0 ? "Super" : KeyBindingSettings.Definitions[index].Group + " / " + KeyBindingSettings.Label(KeyBindingSettings.Definitions[index].Action);
            int y = b.Y + 4 + i;
            if (index == _ui.BindingIndex) Fill(new(b.X + 1, y, b.Width - 2, 1), Selection);
            Text(label, b.X + 2, y, index == _ui.BindingIndex ? Accent : TextColor, b.Width - 4);
            Hit(new(b.X + 1, y, b.Width - 2, 1), (_, _) => _ui.SelectBinding(index));
        }
        PanelBounds field = new(b.X + 1, b.Y + b.Height - 7, b.Width - 2, 3);
        Round(field, Raised, 6); Outline(field, Accent.WithAlpha(150), 6); List<Line> lines = Wrap(_ui.BindingEditor.Text, Math.Max(1, field.Width - 5));
        DrawEditor(field, _ui.BindingEditor, lines, field.Width - 5);
        Hit(field, (x, y) => PlaceEditor(_ui.BindingEditor, lines, field, x, y));
        string[] labels = ["Apply", "Save", "Reset", "Cancel"]; int x1 = b.X + 2;
        for (int i = 0; i < labels.Length; i++)
        {
            int index = i; Text("[" + labels[i] + "]", x1, b.Y + b.Height - 3, Accent);
            Hit(new(x1, b.Y + b.Height - 3, labels[i].Length + 2, 1), (_, _) =>
            { if (index < 2) _ui.ApplyBindings(index == 1); else if (index == 2) _ui.ResetBindings(); else _ui.CloseOverlay(); }); x1 += labels[i].Length + 5;
        }
        Text(_ui.BindingError, b.X + 2, b.Y + b.Height - 2, StatusColor(PreviewDesignStatus.InProgress), b.Width - 4);
    }
    public void PointerDown(float x, float y, bool right = false)
    {
        _pickerDrag = 0;
        _ui.CurrentChat.Selection.End();
        _pointer = new(x, y);
        ClickPixels(x, y, right);
        if (right || _ui.Overlay != WorkspaceOverlay.None) return;
        var pointer = new System.Numerics.Vector2(x, y);
        for (int i = _graphHits.Count - 1; i >= 0; i--)
            if (_graphHits[i].Box.Contains(pointer))
            { _ui.BeginGraphDrag(_graphHits[i].Id, pointer, _graphHits[i].Origin); return; }
    }
    public void Click(int x, int y, bool right = false) => ClickPixels(x * CW, y * CH, right);
    private void ClickPixels(float x, float y, bool right)
    {
        if (right && _ui.Overlay is WorkspaceOverlay.None or WorkspaceOverlay.ElementChat
            && _chatGeometry is { } geometry && geometry.Viewport.Contains(x, y))
        { _ui.FocusHistory(true); _ui.CurrentChat.Selection.End(); _ui.Dirty = true; return; }
        for (int i = _hits.Count - 1; i >= 0; i--)
            if (_hits[i].Bounds.Contains(x, y))
            {
                _hits[i].Action(x, y);
                if (right && _ui.Overlay == WorkspaceOverlay.None && _ui.GraphActive) _ui.OpenOverlay(WorkspaceOverlay.Options);
                _ui.Dirty = true; return;
            }
    }
    private static PanelBounds Intersection(PanelBounds a, PanelBounds b)
    {
        int x = Math.Max(a.X, b.X), y = Math.Max(a.Y, b.Y);
        return new(x, y, Math.Max(0, Math.Min(a.X + a.Width, b.X + b.Width) - x), Math.Max(0, Math.Min(a.Y + a.Height, b.Y + b.Height) - y));
    }
    private static SKRect Pixels(PanelBounds b) => SKRect.Create(b.X * CW, b.Y * CH, b.Width * CW, b.Height * CH);
    private void Hit(PanelBounds b, Action<int, int> action, int offsetY = 0, int heightExtra = 0)
    {
        SKRect bounds = Pixels(b); bounds.Offset(0, offsetY); bounds.Bottom += heightExtra;
        _hits.Add((bounds, (x, y) => action((int)MathF.Floor(x / CW), (int)MathF.Floor((y - offsetY) / CH))));
    }
    private void Fill(PanelBounds b, SKColor color) => Rect(b.X * CW, b.Y * CH, b.Width * CW, b.Height * CH, color);
    private void Rect(float x, float y, float width, float height, SKColor color)
    { _paint.Color = color; _paint.Style = SKPaintStyle.Fill; _canvas.DrawRect(x, y, Math.Max(0, width), Math.Max(0, height), _paint); }
    private void LineSegment(float x1, float y1, float x2, float y2, SKColor color)
    { _paint.Color = color; _paint.StrokeWidth = 1; _canvas.DrawLine(x1, y1, x2, y2, _paint); }
    public void Hover(float x, float y)
    {
        if (_pickerDrag != 0) Pick(x, y);
        if (_ui.CurrentChat.Selection.Dragging) SelectChatAt(x, y, extend: true);
        var point = new System.Numerics.Vector2(x, y);
        if (_pointer == point) return;
        _pointer = point; _ui.Dirty = true;
    }
    private bool Hovered(PanelBounds b, int offsetY = 0)
    { SKRect pixels = Pixels(b); pixels.Offset(0, offsetY); return pixels.Contains(_pointer.X, _pointer.Y); }
    private void Round(PanelBounds b, SKColor color, float radius) => RoundedPixels(Pixels(b), color, radius);
    private void RoundedPixels(SKRect rect, SKColor color, float radius)
    { _paint.Color = color; _paint.Style = SKPaintStyle.Fill; _canvas.DrawRoundRect(rect, radius, radius, _paint); }
    private void Outline(PanelBounds b, SKColor color, float radius)
    {
        _paint.Color = color; _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1;
        SKRect rect = Pixels(b); rect.Inflate(-.5f, -.5f);
        _canvas.DrawRoundRect(rect, radius, radius, _paint); _paint.Style = SKPaintStyle.Fill;
    }
    private void UiText(string text, int x, int y, SKColor color, int max = int.MaxValue, bool small = false, bool title = false)
    {
        if (max <= 0) return;
        SKFont font = title ? _titleFont : small ? _smallFont : _uiFont;
        text = TerminalText.Sanitize(text).Replace('\n', ' ');
        float width = max == int.MaxValue ? float.MaxValue : max * CW;
        string suffix = _unicode ? "…" : "~";
        if (font.MeasureText(text) > width)
        {
            int[] positions = StringInfo.ParseCombiningCharacters(text);
            int length = positions.Length;
            while (length > 0 && font.MeasureText(text[..positions[length - 1]] + suffix) > width) length--;
            text = length > 0 ? text[..positions[length - 1]] + suffix : "";
        }
        UiPixels(text, x * CW, y * CH + 16, color, font);
    }
    private void UiPixels(string text, float x, float baseline, SKColor color, SKFont font)
    { _paint.Color = color; _paint.Style = SKPaintStyle.Fill; _canvas.DrawText(TerminalText.Sanitize(text), x, baseline, SKTextAlign.Left, font, _paint); }
    private void Text(string text, int x, int y, SKColor color, int max = int.MaxValue)
    {
        if (max <= 0) return;
        text = TerminalText.Sanitize(text).Replace('\n', ' ');
        int[] positions = StringInfo.ParseCombiningCharacters(text);
        if (positions.Length > max) text = text[..positions[Math.Max(0, max - 1)]] + (_unicode ? "…" : "~");
        _paint.Color = color; _paint.Style = SKPaintStyle.Fill;
        _canvas.DrawText(text, x * CW, y * CH + 16, SKTextAlign.Left, _font, _paint);
    }
    public void Dispose() { foreach (var font in _markdownFonts.Values) font.Dispose(); _paint.Dispose(); _font.Dispose(); _uiFont.Dispose(); _smallFont.Dispose(); _titleFont.Dispose(); _typeface.Dispose(); }
}
