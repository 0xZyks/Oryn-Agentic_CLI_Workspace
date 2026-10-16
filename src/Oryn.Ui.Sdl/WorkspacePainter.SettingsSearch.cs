using System.Globalization;
using Oryn.App;
using SkiaSharp;

namespace Oryn.Ui.Sdl;

public sealed partial class WorkspacePainter
{
    private SKColor ThemeBase => Color(_ui.DisplayAppearance.ThemeColor);
    private SKColor ThemeInk => Luminance(ThemeBase) > .4 ? new(24, 28, 35) : new(210, 218, 230);
    private static SKColor Color(string hex) { RgbColor c = RgbColor.Parse(hex); return new(c.R, c.G, c.B); }
    private static SKColor Blend(SKColor a, SKColor b, float t) => new(
        (byte)Math.Round(a.Red + (b.Red - a.Red) * t), (byte)Math.Round(a.Green + (b.Green - a.Green) * t),
        (byte)Math.Round(a.Blue + (b.Blue - a.Blue) * t));
    private static double Luminance(SKColor c)
    {
        static double Channel(byte v) { double n = v / 255.0; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); }
        return .2126 * Channel(c.Red) + .7152 * Channel(c.Green) + .0722 * Channel(c.Blue);
    }
    private static double Contrast(SKColor a, SKColor b)
    { double x = Luminance(a), y = Luminance(b); return (Math.Max(x, y) + .05) / (Math.Min(x, y) + .05); }
    private SKColor Readable(SKColor requested, SKColor surface, double minimum = 4.5)
    {
        SKColor ink = Contrast(SKColors.Black, surface) > Contrast(SKColors.White, surface) ? SKColors.Black : SKColors.White;
        for (int i = 0; i <= 10; i++)
        { SKColor candidate = Blend(requested, ink, i / 10f); if (Contrast(candidate, surface) >= minimum) return candidate; }
        return ink;
    }
    private bool DefaultTheme => _ui.DisplayAppearance.ThemeColor == "#090F16";
    private int _pickerDrag;
    private SKRect _pickerBounds;
    public void PointerUp() { _pickerDrag = 0; _ui.CurrentChat.Selection.End(); }
    private void Pick(float x, float y)
    {
        if (_ui.Overlay != WorkspaceOverlay.Appearance) { _pickerDrag = 0; return; }
        if (_pickerDrag == 1) _ui.SetPicker(saturation: (x - _pickerBounds.Left) / _pickerBounds.Width,
            value: 1 - (y - _pickerBounds.Top) / _pickerBounds.Height);
        else if (_pickerDrag == 2) _ui.SetPicker(hue: 359.999f * Math.Clamp((x - _pickerBounds.Left) / _pickerBounds.Width, 0, 1));
    }
    private void DrawAppearance(PanelBounds b)
    {
        bool compact = b.Height < 18;
        int targetY = b.Y + (compact ? 2 : 3);
        int half = Math.Max(1, (b.Width - 4) / 2);
        string[] labels = ["Accent", "Theme / background"];
        for (int i = 0; i < 2; i++)
        {
            int index = i; PanelBounds target = new(b.X + 2 + i * half, targetY, half - 1, 1);
            Fill(target, i == _ui.AppearanceTarget ? Selection : Raised);
            SKColor swatch = Color(i == 0 ? _ui.AppearanceDraft!.AccentColor : _ui.AppearanceDraft!.ThemeColor);
            Rect(target.X * CW + 3, target.Y * CH + 6, 9, 10, swatch);
            UiText(labels[i], target.X + 2, targetY, i == _ui.AppearanceTarget ? Accent : TextColor, target.Width - 2, small: compact);
            Hit(target, (_, _) => _ui.SelectAppearanceTarget(index));
        }
        if (!compact)
        {
            SKRect square = Pixels(new(b.X + 2, b.Y + 5, b.Width - 4, b.Height - 15));
            using (SKPaint gradient = new())
            {
                using SKShader shader = SKShader.CreateLinearGradient(new(square.Left, square.Top), new(square.Right, square.Top),
                    [SKColors.White, Color(RgbColor.FromHsv(_ui.PickerHue, 1, 1).Hex)], null, SKShaderTileMode.Clamp);
                gradient.Shader = shader; _canvas.DrawRect(square, gradient);
            }
            using (SKPaint shade = new())
            {
                using SKShader shader = SKShader.CreateLinearGradient(new(square.Left, square.Top), new(square.Left, square.Bottom),
                    [SKColors.Transparent, SKColors.Black], null, SKShaderTileMode.Clamp);
                shade.Shader = shader; _canvas.DrawRect(square, shade);
            }
            float cx = square.Left + _ui.PickerSaturation * square.Width, cy = square.Top + (1 - _ui.PickerValue) * square.Height;
            Circle(cx, cy, 5, SKColors.Black); Circle(cx, cy, 3.5f, SKColors.White); Circle(cx, cy, 2, Color(RgbColor.FromHsv(_ui.PickerHue, _ui.PickerSaturation, _ui.PickerValue).Hex));
            _hits.Add((square, (x, y) => { _pickerDrag = 1; _pickerBounds = square; Pick(x, y); }));
            SKRect hue = Pixels(new(b.X + 2, b.Y + b.Height - 9, b.Width - 4, 1));
            SKColor[] stops = Enumerable.Range(0, 7).Select(i => Color(RgbColor.FromHsv(i * 60, 1, 1).Hex)).ToArray();
            using (SKPaint paint = new())
            {
                using SKShader shader = SKShader.CreateLinearGradient(new(hue.Left, hue.Top), new(hue.Right, hue.Top), stops, null, SKShaderTileMode.Clamp);
                paint.Shader = shader; _canvas.DrawRect(hue, paint);
            }
            float hx = hue.Left + _ui.PickerHue / 360 * hue.Width;
            Rect(hx - 2, hue.Top, 4, hue.Height, SKColors.Black); Rect(hx - 1, hue.Top, 2, hue.Height, SKColors.White);
            _hits.Add((hue, (x, y) => { _pickerDrag = 2; _pickerBounds = hue; Pick(x, y); }));
            UiText("Tab: accent/theme / HEX: any color", b.X + 2, b.Y + 4, Muted, b.Width - 4, small: true);
        }
        PanelBounds field = new(b.X + 1, compact ? b.Y + 3 : b.Y + b.Height - 7, b.Width - 2, 3);
        Round(field, Raised, 2); Outline(field, Accent, 2);
        List<Line> lines = Wrap(_ui.AppearanceEditor.Text, Math.Max(1, field.Width - 5));
        DrawEditor(field, _ui.AppearanceEditor, lines, field.Width - 5);
        Hit(field, (x, y) => PlaceEditor(_ui.AppearanceEditor, lines, field, x, y));
        string[] buttons = ["Apply", "Save", "Reset", "Cancel"]; int bx = b.X + 2;
        for (int i = 0; i < buttons.Length; i++)
        {
            int index = i; Text("[" + buttons[i] + "]", bx, b.Y + b.Height - 3, Accent);
            Hit(new(bx, b.Y + b.Height - 3, buttons[i].Length + 2, 1), (_, _) =>
            { if (index < 2) _ui.ApplyAppearance(index == 1); else if (index == 2) _ui.ResetAppearance(); else _ui.CloseOverlay(); });
            bx += buttons[i].Length + 3;
        }
        UiText(_ui.AppearanceError.Length > 0 ? _ui.AppearanceError : "Preview / Alt+A apply / Alt+S save", b.X + 2, b.Y + b.Height - 2,
            _ui.AppearanceError.Length > 0 ? StatusColor(PreviewDesignStatus.InProgress) : Muted, b.Width - 4, small: true);
    }
    private void DrawSearchField(PanelBounds b)
    {
        PanelBounds field = new(b.X + 1, b.Y + Spacing.ExplorerHeaderRows - 1, Math.Max(1, b.Width - 2), 1);
        _canvas.Save(); _canvas.Translate(0, Spacing.Gap);
        Fill(field, Raised); Outline(field, _ui.SearchFocused && _ui.State.Focus == WorkspacePanel.Explorer ? Accent : Border, 2);
        float x = (field.X + 1) * CW, y = field.Y * CH + 10;
        _paint.Color = Muted; _paint.Style = SKPaintStyle.Stroke; _paint.StrokeWidth = 1;
        _canvas.DrawCircle(x, y, 3.5f, _paint); _paint.Style = SKPaintStyle.Fill;
        LineSegment(x + 2.5f, y + 2.5f, x + 6, y + 6, Muted);
        int[] starts = StringInfo.ParseCombiningCharacters(_ui.SearchEditor.Text);
        int cursor = new StringInfo(_ui.SearchEditor.Text[.._ui.SearchEditor.Cursor]).LengthInTextElements;
        int inner = Math.Max(1, field.Width - 6), first = Math.Max(0, cursor - inner + 1);
        int start = first < starts.Length ? starts[first] : _ui.SearchEditor.Text.Length;
        if (_ui.SearchEditor.Text.Length == 0) UiText("Search / Ctrl+F", field.X + 3, field.Y, Muted, inner + 1, small: true);
        else
        {
            int anchor = new StringInfo(_ui.SearchEditor.Text[.._ui.SearchEditor.Anchor]).LengthInTextElements;
            int a = Math.Clamp(Math.Min(cursor, anchor) - first, 0, inner), z = Math.Clamp(Math.Max(cursor, anchor) - first, 0, inner);
            if (z > a) Fill(new(field.X + 3 + a, field.Y, z - a, 1), Selection);
            Text(_ui.SearchEditor.Text[start..], field.X + 3, field.Y, TextColor, inner);
        }
        if (_ui.SearchFocused && _ui.AcceptsText)
        {
            Rect((field.X + 3 + cursor - first) * CW, field.Y * CH + 4, 1, CH - 8, Accent);
            if (_ui.Composition.Length > 0) Text(_ui.Composition, field.X + 3 + cursor - first, field.Y, Accent, inner);
        }
        _canvas.Restore();
        Hit(field, (px, _) =>
        {
            _ui.FocusSearch();
            int column = Math.Clamp(px - field.X - 3 + first, 0, starts.Length);
            _ui.SearchEditor.Place(column < starts.Length ? starts[column] : _ui.SearchEditor.Text.Length);
        }, offsetY: Spacing.Gap);
        if (_ui.SearchActive)
        {
            _canvas.Save(); _canvas.Translate(0, Spacing.Gap);
            Text("x", field.X + field.Width - 2, field.Y, Muted, 1); _canvas.Restore();
            Hit(new(field.X + field.Width - 3, field.Y, 3, 1), (_, _) => _ui.ClearSearch(), offsetY: Spacing.Gap);
        }
    }
    private void DrawSearchResults(PanelBounds b)
    {
        _canvas.Save(); _canvas.Translate(0, Spacing.TreeOffset);
        UiText(_ui.Searching ? "Searching..." : _ui.SearchResult?.Summary ?? "", b.X + 1, b.Y + Spacing.SearchHeaderRows - 1, Muted, b.Width - 2, small: true);
        _canvas.Restore();
        _canvas.Save(); _canvas.Translate(0, Spacing.ResultsOffset);
        IReadOnlyList<WorkspaceSearchMatch> matches = _ui.SearchResult?.Matches ?? [];
        if (!_ui.Searching && matches.Count == 0) UiText("No matching files", b.X + 1, b.Y + Spacing.SearchHeaderRows + 1, Muted, b.Width - 2);
        int visible = Spacing.SearchRows(b.Height);
        for (int i = 0; i < visible && i + _ui.SearchTop < matches.Count; i++)
        {
            int index = i + _ui.SearchTop, y = b.Y + Spacing.SearchHeaderRows + i * 2;
            WorkspaceSearchMatch match = matches[index]; PanelBounds item = new(b.X + 1, y, b.Width - 2, 2);
            if (index == _ui.SearchIndex || Hovered(item, Spacing.ResultsOffset)) Fill(item, index == _ui.SearchIndex ? Selection : Raised);
            FileIcon((b.X + 2) * CW, y * CH + 11, match.Entry.Name);
            UiText(match.Entry.Name, b.X + 4, y, index == _ui.SearchIndex ? Accent : TextColor, b.Width - 5);
            UiText(match.RelativePath + (match.Line is { } line ? $":{line} / text" : " / name"), b.X + 2, y + 1, Muted, b.Width - 3, small: true);
            Hit(item, (_, _) => _ui.SelectSearchResult(index), offsetY: Spacing.ResultsOffset);
        }
        _canvas.Restore();
    }
    private void FileIcon(float x, float y, string name)
    {
        string ext = Path.GetExtension(name).ToLowerInvariant();
        SKColor color = !_color ? Muted : Readable(SKColor.Parse(ext switch
        {
            ".cs" or ".csproj" => "#A998DF", ".rs" => "#DFA780", ".c" or ".h" or ".cpp" or ".hpp" => "#8DB9E0",
            ".js" or ".jsx" or ".json" => "#D9C780", ".ts" or ".tsx" => "#80B4DA", ".py" => "#DBC078",
            ".md" or ".txt" => "#9FB8CC", ".sh" or ".ps1" => "#92C3A0", ".yaml" or ".yml" or ".toml" or ".xml" => "#D5AE94",
            ".png" or ".jpg" or ".jpeg" or ".svg" or ".gif" or ".webp" => "#C69DC4", _ => "#899BB1"
        }), Inactive, 3);
        // Folded page outline and internal vector marks; no private-use/emoji font dependency.
        LineSegment(x - 5, y - 7, x + 1, y - 7, color); LineSegment(x + 1, y - 7, x + 5, y - 3, color);
        LineSegment(x + 5, y - 3, x + 5, y + 7, color); LineSegment(x + 5, y + 7, x - 5, y + 7, color);
        LineSegment(x - 5, y + 7, x - 5, y - 7, color); LineSegment(x + 1, y - 7, x + 1, y - 3, color);
        LineSegment(x + 1, y - 3, x + 5, y - 3, color);
        switch (ext)
        {
            case ".cs": case ".csproj":
                LineSegment(x - 2, y - 1, x - 2, y + 5, color); LineSegment(x + 1, y - 1, x + 1, y + 5, color);
                LineSegment(x - 3, y + 1, x + 2, y + 1, color); LineSegment(x - 3, y + 3, x + 2, y + 3, color); break;
            case ".rs":
                Circle(x, y + 2, 2, color); LineSegment(x - 3, y - 1, x + 3, y + 5, color); LineSegment(x + 3, y - 1, x - 3, y + 5, color); break;
            case ".js": case ".jsx": case ".ts": case ".tsx": case ".py": case ".c": case ".h": case ".cpp": case ".hpp": case ".html": case ".css":
                LineSegment(x - 1, y, x - 3, y + 2, color); LineSegment(x - 3, y + 2, x - 1, y + 4, color);
                LineSegment(x + 1, y, x + 3, y + 2, color); LineSegment(x + 3, y + 2, x + 1, y + 4, color); break;
            case ".json": case ".xml": case ".yml": case ".yaml": case ".toml":
                LineSegment(x - 3, y, x - 1, y, color); LineSegment(x - 3, y + 3, x + 2, y + 3, color); Circle(x + 2, y, 1, color); break;
            case ".png": case ".jpg": case ".jpeg": case ".svg": case ".gif": case ".webp":
                Circle(x + 2, y, 1, color); LineSegment(x - 3, y + 5, x, y + 1, color); LineSegment(x, y + 1, x + 3, y + 5, color); break;
            case ".sh": case ".ps1":
                LineSegment(x - 3, y, x - 1, y + 2, color); LineSegment(x - 1, y + 2, x - 3, y + 4, color); LineSegment(x, y + 4, x + 3, y + 4, color); break;
            default:
                LineSegment(x - 3, y, x + 2, y, color); LineSegment(x - 3, y + 3, x + 2, y + 3, color); break;
        }
    }
}
