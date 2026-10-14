using System.Globalization;
using Oryn.App;
using SkiaSharp;

namespace Oryn.Ui.Sdl;

/// <summary>Skia drawing and hit regions; application state lives in Oryn.App.</summary>
public sealed class WorkspacePainter : IDisposable
{
    private const int CW = GraphicalWorkspaceController.CellWidth, CH = GraphicalWorkspaceController.CellHeight;
    private readonly SKTypeface _typeface;
    private readonly SKFont _font;
    private readonly SKPaint _paint = new() { IsAntialias = true };
    private SKCanvas _canvas = null!;
    private long _animationTime;
    private readonly List<(PanelBounds Bounds, Action<int, int> Action)> _hits = [];
    private readonly List<(Guid Id, GraphBox Box, System.Numerics.Vector2 Origin)> _graphHits = [];
    private readonly GraphicalWorkspaceController _ui;
    private readonly bool _color;
    private readonly bool _unicode;
    private SKColor Background => SKColor.Parse(_color ? "#101218" : "#101010");
    private SKColor Active => SKColor.Parse(_color ? "#1c1f29" : "#202020");
    private SKColor Inactive => SKColor.Parse(_color ? "#15171e" : "#151515");
    private SKColor TextColor => SKColor.Parse("#d4d7e3");
    private SKColor Muted => SKColor.Parse("#858b9e");
    private SKColor Accent => SKColor.Parse(_color ? "#b9a4ef" : "#ffffff");
    private SKColor Selection => SKColor.Parse(_color ? "#39344c" : "#454545");
    public WorkspacePainter(GraphicalWorkspaceController ui, bool color = true, bool unicode = true)
    {
        _ui = ui; _color = color; _unicode = unicode;
        string[] preferred = ["JetBrains Mono", "Noto Sans Mono", "DejaVu Sans Mono", "Menlo", "Consolas", "monospace"];
        string[] families = SKFontManager.Default.FontFamilies.ToArray();
        string family = preferred.FirstOrDefault(p => families.Contains(p, StringComparer.OrdinalIgnoreCase)) ?? "monospace";
        _typeface = SKTypeface.FromFamilyName(family); _font = new(_typeface, 15) { Edging = SKFontEdging.SubpixelAntialias };
    }
    public void Draw(SKCanvas canvas, int width, int height, float scaleX = 1, float scaleY = 1, long? animationTime = null)
    {
        _animationTime = animationTime ?? GraphicalWorkspaceController.Now;
        _canvas = canvas; _hits.Clear(); _graphHits.Clear();
        canvas.Clear(Background); canvas.Save(); canvas.Scale(scaleX, scaleY);
        _ui.Resize(width, height);
        int columns = _ui.Columns, rows = _ui.Rows;
        Fill(new(0, 0, columns, 2), Inactive);
        Text("oryn", 2, 0, Accent); Text("/ " + Path.GetFileName(_ui.State.ProjectRoot), 9, 0, TextColor, Math.Max(1, columns - 34));
        Text("M0 / local", Math.Max(2, columns - 14), 0, Muted);
        var menus = GraphicalWorkspaceController.NavbarLabels;
        int selectedEnd = 2 + menus.Take(_ui.NavbarIndex + 1).Sum(menu => menu.Length + 3);
        int mx = 2 - (_ui.NavbarFocused || _ui.OpenNavbarIndex is not null ? Math.Max(0, selectedEnd - columns + 1) : 0);
        _canvas.Save(); _canvas.ClipRect(Pixels(new(0, 1, columns, 1)));
        for (int i = 0; i < menus.Count; i++)
        {
            string menu = menus[i]; int index = i;
            bool selected = (_ui.NavbarFocused && _ui.NavbarIndex == i) || _ui.OpenNavbarIndex == i;
            if (selected) Fill(new(mx - 1, 1, menu.Length + 2, 1), Selection);
            Text(menu, mx, 1, selected ? Accent : Muted);
            Hit(new(Math.Max(0, mx), 1, Math.Max(0, Math.Min(columns, mx + menu.Length + 1) - Math.Max(0, mx)), 1), (_, _) => _ui.OpenNavbarMenu(index));
            mx += menu.Length + 3;
        }
        _canvas.Restore();
        WorkspaceLayout layout = _ui.Layout;
        Panel(layout.Explorer, WorkspacePanel.Explorer, "Files", DrawExplorer);
        Panel(layout.Center, WorkspacePanel.Center, "", DrawCenter);
        Panel(layout.Visualizer, WorkspacePanel.Visualizer, "Visualizer", DrawGraph);
        Splitter(layout.ExplorerSplitter); Splitter(layout.VisualizerSplitter);
        if (rows >= 3)
        {
            Fill(new(0, rows - 2, columns, 2), Background);
            Text((_ui.Loading ? "loading / " : "") + _ui.State.Status, 2, rows - 2, Muted, columns - 4);
            string scope = _ui.State.FocusArea == PanelFocusArea.Tabs ? "Tabs"
                : _ui.State.Focus == WorkspacePanel.Explorer ? "Files"
                : _ui.GraphActive ? _ui.State.Graph.SelectedMemberId is null ? "Graph cards" : "Graph members"
                : _ui.State.ActiveTab == WorkspaceTab.Chat ? _ui.HistoryFocused ? "Chat history" : "Chat input" : "Code";
            string hint = _ui.Keyboard.LeaderPending ? "Super leader pending / Esc cancel"
                : _ui.Overlay == WorkspaceOverlay.Keybindings ? "Keybindings / Tab fields / Alt+A apply / Alt+S save / Esc cancel"
                : _ui.Overlay == WorkspaceOverlay.ElementChat ? "Component chat / Enter send / Shift+Enter newline / Esc close"
                : _ui.Overlay != WorkspaceOverlay.None ? "Dialog / arrows select / Enter confirm / Esc close"
                : _ui.NavbarFocused ? "Menu bar / arrows or Tab select / Enter open / Shift+Tab or Esc return"
                : _ui.State.FocusArea == PanelFocusArea.Tabs ? "Tabs / arrows select / Enter content / Esc content"
                : $"{scope} / {_ui.Keyboard.DisplayHint(KeyboardAction.NextPanel).Split(" / ")[0]} panels / {_ui.Keyboard.DisplayHint(KeyboardAction.ToggleTabFocus).Split(" / ")[^1]} tabs";
            Text(hint, 2, rows - 1, Muted, columns - 4);
        }
        if (_ui.Overlay != WorkspaceOverlay.None) DrawOverlay();
        canvas.Restore();
        _ui.Dirty = false;
    }
    private void Panel(PanelBounds b, WorkspacePanel panel, string title, Action<PanelBounds> draw)
    {
        if (b.Width <= 0 || b.Height <= 0) return;
        bool focused = !_ui.NavbarFocused && _ui.State.Focus == panel;
        Fill(b, focused ? Active : Inactive);
        if (focused) Rect(b.X * CW, b.Y * CH - 3, b.Width * CW, 3, Accent);
        Hit(b, (_, _) => _ui.FocusPanel(panel));
        _canvas.Save(); _canvas.ClipRect(Pixels(b));
        if (title.Length > 0) Text(title, b.X + 1, b.Y, focused ? Accent : Muted);
        draw(b); _canvas.Restore();
    }
    private void Splitter(PanelBounds b)
    {
        if (b.Width == 0) return;
        Rect((b.X + b.Width / 2f) * CW, b.Y * CH, 1, b.Height * CH, Inactive);
        Hit(b, (x, y) => _ui.State.BeginResize(x, y, _ui.Columns, _ui.Rows));
    }
    private void DrawExplorer(PanelBounds b)
    {
        Text(Path.GetFileName(_ui.State.ProjectRoot) + "/", b.X + 1, b.Y + 1, Muted, b.Width - 2);
        int count = Math.Max(0, b.Height - 3);
        for (int i = 0; i < count && i + _ui.ExplorerTop < _ui.State.Rows.Count; i++)
        {
            int index = i + _ui.ExplorerTop;
            ExplorerRow row = _ui.State.Rows[index]; int y = b.Y + 3 + i;
            if (index == _ui.State.SelectedIndex) Fill(new(b.X, y, b.Width, 1), Selection);
            string arrow = row.Entry.IsDirectory ? _ui.State.ExpandedDirectories.Contains(row.Entry.Path) ? "v " : "> " : "  ";
            Text(new string(' ', Math.Min(row.Depth * 2, b.Width)) + arrow + row.Entry.Name + (row.Entry.IsDirectory ? "/" : ""), b.X + 1, y,
                index == _ui.State.SelectedIndex ? Accent : TextColor, b.Width - 2);
            Hit(new(b.X, y, b.Width, 1), (_, _) => _ui.SelectExplorer(index));
        }
    }
    private void DrawCenter(PanelBounds b)
    {
        WorkspaceTab[] tabs = [WorkspaceTab.Chat, WorkspaceTab.Code, WorkspaceTab.Visualizer]; int x = b.X + 1;
        foreach (WorkspaceTab tab in tabs)
        {
            string name = tab.ToString(); bool selected = _ui.State.ActiveTab == tab;
            if (selected) Fill(new(x, b.Y, name.Length + 2, 1), Selection);
            Text(name, x + 1, b.Y, selected ? Accent : Muted);
            if (selected && !_ui.NavbarFocused && _ui.State.FocusArea == PanelFocusArea.Tabs && _ui.State.Focus == WorkspacePanel.Center)
                Rect(x * CW, (b.Y + 1) * CH + 2, (name.Length + 2) * CW, 1, Muted);
            Hit(new(x, b.Y, name.Length + 2, 1), (_, _) => _ui.SelectTab(tab)); x += name.Length + 4;
        }
        PanelBounds content = new(b.X, b.Y + 2, b.Width, Math.Max(0, b.Height - 2));
        _canvas.Save(); _canvas.ClipRect(Pixels(content));
        switch (_ui.State.ActiveTab)
        {
            case WorkspaceTab.Chat: DrawChat(content, _ui.State.Chat); break;
            case WorkspaceTab.Code: DrawCode(content); break;
            case WorkspaceTab.Visualizer: DrawGraph(content); break;
        }
        _canvas.Restore();
    }
    private void DrawCode(PanelBounds b)
    {
        Text(_ui.State.Preview is { } preview ? Path.GetRelativePath(_ui.State.ProjectRoot, preview.Path) + " / read-only" : "Code / read-only", b.X + 1, b.Y, Muted, b.Width - 2);
        PanelBounds surface = new(b.X + 1, b.Y + 2, Math.Max(0, b.Width - 2), Math.Max(0, b.Height - 3));
        Fill(surface, Background); _canvas.Save(); _canvas.ClipRect(Pixels(surface));
        string[] lines = (_ui.State.Preview?.Content ?? "Select a file in the explorer to open its preview.").Replace("\r", "").Split('\n');
        int top = Math.Min(_ui.CodeTop, Math.Max(0, lines.Length - Math.Max(1, surface.Height - 1)));
        for (int i = 0; i < surface.Height - 1 && i + top < lines.Length; i++)
        {
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
    private void DrawChat(PanelBounds b, ChatPreviewState chat)
    {
        if (b.Width < 4 || b.Height < 3) { Text("Enlarge window", b.X, b.Y, Muted); return; }
        Text("demo / local session / no model", b.X + 1, b.Y, Muted, b.Width - 2);
        int inner = Math.Max(1, b.Width - 5);
        List<Line> draft = Wrap(_ui.Editor.Text, inner);
        int inputHeight = Math.Min(Math.Max(3, draft.Count + 2), Math.Max(3, b.Height / 2));
        PanelBounds input = new(b.X + 1, b.Y + b.Height - inputHeight, b.Width - 2, inputHeight);
        List<(string Text, bool Right, bool Label, bool Thinking)> history = [];
        if (chat.Messages.Count == 0)
        {
            history.Add(("Welcome to Oryn.", false, false, false));
            history.Add(("Explore your project. Keep your reasoning close to the code.", false, false, false));
            history.Add(("This M0 conversation replies with Hello World!", false, false, false));
        }
        foreach (ChatPreviewMessage message in chat.Messages)
        {
            bool right = message.Role == ChatPreviewRole.User;
            history.Add((right ? "User" : "Oryn", right, true, false));
            foreach (Line line in Wrap(message.Text, Math.Max(1, Math.Min(inner, 76)))) history.Add((line.Text, right, false, false));
            history.Add(("", false, false, false));
        }
        if (chat.IsThinking)
        {
            history.Add(("Oryn", false, true, false));
            history.Add(("Thinking...", false, false, true));
        }
        int space = Math.Max(0, input.Y - b.Y - 3), top = Math.Max(0, history.Count - space - _ui.ChatScroll);
        _canvas.Save(); _canvas.ClipRect(Pixels(new(b.X, b.Y + 2, b.Width, space)));
        for (int i = 0; i < space && top + i < history.Count; i++)
        {
            var line = history[top + i];
            int x = line.Right ? Math.Max(b.X + 1, b.X + b.Width - 2 - (int)Math.Ceiling((_font.MeasureText(line.Text) + (line.Label ? 2 * CW : 0)) / CW)) : b.X + 2;
            int y = b.Y + 2 + i;
            if (line.Label)
            {
                if (line.Right) Circle(x * CW + 5, y * CH + 11, 2.3f, Accent);
                else Star(x * CW + 5, y * CH + 11, 5.5f, Accent);
            }
            else if (line.Thinking) ThinkingSpinner(x * CW + 5, y * CH + 11);
            Text(line.Text, x + (line.Label || line.Thinking ? 2 : 0), y, line.Label ? Accent : TextColor, b.Width - 3 - (line.Label || line.Thinking ? 2 : 0));
        }
        _canvas.Restore();
        Hit(new(b.X, b.Y + 2, b.Width, space), (_, _) => { if (_ui.Overlay == WorkspaceOverlay.None) { _ui.FocusHistory(true); } });
        Fill(input, SKColor.Parse(_color ? "#282b37" : "#303030"));
        DrawEditor(input, _ui.Editor, draft, inner);
        Hit(input, (x, y) =>
        {
            if (_ui.Overlay == WorkspaceOverlay.None) { _ui.FocusHistory(false); }
            PlaceEditor(_ui.Editor, draft, input, x, y);
        });
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
            if (i == cursorLine && _ui.AcceptsText)
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
    private void DrawGraph(PanelBounds b)
    {
        Text("DEMO / fictitious graph / " + _ui.State.Graph.Zoom, b.X + 1, b.Y + 1, Muted, b.Width - 2);
        string[] controls = ["Tab", "Dock", "Left", "Right", "+", "-"]; int x = b.X + 1;
        KeyboardAction[] actions = [KeyboardAction.FocusVisualizer, KeyboardAction.ToggleDock, KeyboardAction.DockLeft, KeyboardAction.DockRight, KeyboardAction.GraphZoomIn, KeyboardAction.GraphZoomOut];
        for (int i = 0; i < controls.Length; i++)
        {
            int index = i; Text(controls[i], x, b.Y + 2, Accent); Hit(new(x, b.Y + 2, controls[i].Length + 1, 1), (_, _) =>
            { if (index == 0) _ui.PlaceVisualizer(VisualizerPlacement.Tab); else _ui.Execute(actions[index], GraphicalWorkspaceController.Now); });
            x += controls[i].Length + 2;
        }
        PanelBounds view = new(b.X + 1, b.Y + 4, Math.Max(1, b.Width - 2), Math.Max(1, b.Height - 5));
        PreviewGraphLayout graph = new(_ui.State.Graph.Zoom, view.Width);
        GraphDragState placement = _ui.State.Graph.Placement;
        placement.Configure(_ui.State.Graph.Nodes, graph);
        GraphBox selectedBox = placement.Bounds(_ui.State.Graph.SelectedId);
        int sceneWidth = (int)Math.Ceiling(_ui.State.Graph.Nodes.Max(n => placement.Bounds(n.Id).Right) / CW) + 2;
        int sceneHeight = (int)Math.Ceiling(_ui.State.Graph.Nodes.Max(n => placement.Bounds(n.Id).Bottom) / CH) + 2;
        if (placement.DraggedId is null)
            _ui.State.Graph.EnsureVisible(new((int)(selectedBox.X / CW), (int)(selectedBox.Y / CH), graph.CardWidth, graph.CardHeight),
                view.Width, view.Height, sceneWidth, sceneHeight,
                Math.Min(0, (int)Math.Floor(_ui.State.Graph.Nodes.Min(n => placement.Bounds(n.Id).X) / CW) - 2),
                Math.Min(0, (int)Math.Floor(_ui.State.Graph.Nodes.Min(n => placement.Bounds(n.Id).Y) / CH) - 2));
        int ox = view.X - _ui.State.Graph.ScrollX, oy = view.Y - _ui.State.Graph.ScrollY;
        System.Numerics.Vector2 origin = new(ox * CW, oy * CH);
        _canvas.Save(); _canvas.ClipRect(Pixels(view));
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
            LineSegment(a.X, a.Y, z.X, z.Y, Muted);
            var unit = System.Numerics.Vector2.Normalize(z - a);
            var perpendicular = new System.Numerics.Vector2(-unit.Y, unit.X);
            var arrow = z - unit * 7;
            LineSegment(arrow.X + perpendicular.X * 4, arrow.Y + perpendicular.Y * 4, z.X, z.Y, Muted);
            LineSegment(arrow.X - perpendicular.X * 4, arrow.Y - perpendicular.Y * 4, z.X, z.Y, Muted);
            Text(link.Label, (int)((a.X + z.X) / 2 / CW), (int)((a.Y + z.Y) / 2 / CH), Muted, graph.Gap + 4);
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
            Fill(card, selected ? Selection : SKColor.Parse(_color ? "#252833" : "#272727"));
            Rect(card.X * CW, card.Y * CH, 2, card.Height * CH, selected ? Accent : Muted.WithAlpha(60));
            Text(node.Name, card.X + 1, card.Y, selected ? Accent : TextColor, card.Width - 2);
            Text(Status(node.Status), card.X + 1, card.Y + 1, StatusColor(node.Status), card.Width - 2);
            if (_ui.State.Graph.Zoom != PreviewZoom.Overview) Text(node.Kind, card.X + 1, card.Y + 2, Muted, card.Width - 2);
            PanelBounds clickable = Intersection(card, view);
            if (clickable.Width > 0 && clickable.Height > 0) Hit(clickable, (_, _) => { _ui.FocusPanel(_ui.State.LayoutOptions.VisualizerPlacement == VisualizerPlacement.Tab ? WorkspacePanel.Center : WorkspacePanel.Visualizer); _ui.State.Graph.Select(node.Id); });
            if (_ui.State.Graph.Zoom == PreviewZoom.Details)
                for (int i = 0; i < node.Members.Length; i++)
                {
                    PreviewGraphMember member = node.Members[i]; int y = card.Y + 4 + i;
                    bool active = member.Id == _ui.State.Graph.SelectedMemberId;
                    if (active) Fill(new(card.X, y, card.Width, 1), Muted.WithAlpha(70));
                    Text((active ? "> " : "  ") + member.Name, card.X + 1, y, active ? Accent : TextColor, card.Width - 2);
                    PanelBounds memberHit = Intersection(new(card.X, y, card.Width, 1), view);
                    if (memberHit.Height > 0 && memberHit.Width > 0) Hit(memberHit, (_, _) => { _ui.FocusPanel(_ui.State.LayoutOptions.VisualizerPlacement == VisualizerPlacement.Tab ? WorkspacePanel.Center : WorkspacePanel.Visualizer); _ui.State.Graph.Select(node.Id); _ui.State.Graph.SelectMember(member.Id); });
                }
            _canvas.Restore();
        }
        _canvas.Restore();
    }
    private static string Status(PreviewDesignStatus status) => status switch
    { PreviewDesignStatus.ToValidate => "À valider", PreviewDesignStatus.Validated => "Validé", PreviewDesignStatus.InProgress => "En cours", _ => "Implémenté" };
    private SKColor StatusColor(PreviewDesignStatus status) => !_color ? TextColor : SKColor.Parse(status switch
    { PreviewDesignStatus.ToValidate => "#a49cba", PreviewDesignStatus.Validated => "#88b8dc", PreviewDesignStatus.InProgress => "#debf87", _ => "#92bfa2" });
    private void DrawOverlay()
    {
        _hits.Clear(); // Modal hit regions never leak through to underlying panels.
        Rect(0, 0, _ui.Columns * CW, _ui.Rows * CH, new SKColor(0, 0, 0, 170));
        int width = Math.Min(_ui.Columns - 2, _ui.Overlay is WorkspaceOverlay.ElementChat or WorkspaceOverlay.Keybindings ? 100 : 66);
        int height = Math.Min(_ui.Rows - 2, _ui.Overlay is WorkspaceOverlay.ElementChat or WorkspaceOverlay.Keybindings ? 30 : 15);
        width = Math.Max(1, width); height = Math.Max(1, height);
        PanelBounds box = new((_ui.Columns - width) / 2, (_ui.Rows - height) / 2, width, height);
        Fill(box, Active); Rect(box.X * CW, box.Y * CH, box.Width * CW, 2, Accent);
        _canvas.Save(); _canvas.ClipRect(Pixels(box));
        Text(_ui.Overlay switch { WorkspaceOverlay.Menu => _ui.MenuTitle, WorkspaceOverlay.Options => "Component options", WorkspaceOverlay.Details => "Component / demo details", WorkspaceOverlay.ElementChat => "Component chat / demo", _ => "Keybindings" }, box.X + 2, box.Y + 1, Accent, box.Width - 9);
        Text("[Esc]", box.X + box.Width - 7, box.Y + 1, Muted);
        Hit(new(box.X + box.Width - 8, box.Y, 8, 2), (_, _) => _ui.CloseOverlay());
        if (_ui.Overlay is WorkspaceOverlay.Menu or WorkspaceOverlay.Options)
        {
            string[] items = _ui.Overlay == WorkspaceOverlay.Menu ? _ui.MenuItems.Select(item => item.Label).ToArray() : ["Show details", "Show chat (demo)", "Back"];
            for (int i = 0; i < items.Length; i++)
            {
                int index = i; int y = box.Y + 4 + i;
                if (i == _ui.OverlaySelection) Fill(new(box.X + 1, y, box.Width - 2, 1), Selection);
                Text((i == _ui.OverlaySelection ? "> " : "  ") + items[i], box.X + 2, y,
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
        _canvas.Restore();
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
        Fill(field, Selection); List<Line> lines = Wrap(_ui.BindingEditor.Text, Math.Max(1, field.Width - 5));
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
        Click((int)(x / CW), (int)(y / CH), right);
        if (right || _ui.Overlay != WorkspaceOverlay.None) return;
        var pointer = new System.Numerics.Vector2(x, y);
        for (int i = _graphHits.Count - 1; i >= 0; i--)
            if (_graphHits[i].Box.Contains(pointer))
            { _ui.BeginGraphDrag(_graphHits[i].Id, pointer, _graphHits[i].Origin); return; }
    }
    public void Click(int x, int y, bool right = false)
    {
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
    private void Hit(PanelBounds b, Action<int, int> action) => _hits.Add((b, action));
    private void Fill(PanelBounds b, SKColor color) => Rect(b.X * CW, b.Y * CH, b.Width * CW, b.Height * CH, color);
    private void Rect(float x, float y, float width, float height, SKColor color)
    { _paint.Color = color; _paint.Style = SKPaintStyle.Fill; _canvas.DrawRect(x, y, Math.Max(0, width), Math.Max(0, height), _paint); }
    private void LineSegment(float x1, float y1, float x2, float y2, SKColor color)
    { _paint.Color = color; _paint.StrokeWidth = 1; _canvas.DrawLine(x1, y1, x2, y2, _paint); }
    private void Text(string text, int x, int y, SKColor color, int max = int.MaxValue)
    {
        if (max <= 0) return;
        text = TerminalText.Sanitize(text).Replace('\n', ' ');
        int[] positions = StringInfo.ParseCombiningCharacters(text);
        if (positions.Length > max) text = text[..positions[Math.Max(0, max - 1)]] + (_unicode ? "…" : "~");
        _paint.Color = color; _paint.Style = SKPaintStyle.Fill;
        _canvas.DrawText(text, x * CW, y * CH + 16, SKTextAlign.Left, _font, _paint);
    }
    public void Dispose() { _paint.Dispose(); _font.Dispose(); _typeface.Dispose(); }
}
