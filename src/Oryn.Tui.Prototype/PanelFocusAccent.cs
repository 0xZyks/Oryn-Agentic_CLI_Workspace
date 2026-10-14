using Oryn.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Oryn.Tui.Prototype;

/// <summary>A full-width accent above the focused panel, separate from its content and titles.</summary>
internal sealed class PanelFocusAccent(WorkspaceTheme theme, bool unicode)
{
    private readonly Label _bar = new() { CanFocus = false, Visible = false, BorderStyle = LineStyle.None, Height = 1 };
    private PanelBounds? _rendered;

    public void AddTo(View parent)
    {
        parent.Add(_bar);
    }

    public void Update(PanelBounds bounds)
    {
        if (_rendered == bounds) return;
        _rendered = bounds;
        _bar.Visible = bounds.Width > 0 && bounds.Height > 0 && bounds.Y > 0;
        if (!_bar.Visible) return;
        _bar.SetScheme(theme.FocusAccent);
        _bar.X = bounds.X;
        _bar.Y = bounds.Y - 1;
        _bar.Width = bounds.Width;
        // The bottom block touches the panel below; a top block looked attached
        // to the navigation bar above the gap instead.
        _bar.Text = new string(unicode ? '▁' : '=', bounds.Width);
    }
}
