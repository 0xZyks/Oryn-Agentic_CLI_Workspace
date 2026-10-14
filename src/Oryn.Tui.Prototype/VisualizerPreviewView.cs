using System.Drawing;
using Oryn.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Oryn.Tui.Prototype;

/// <summary>Native toolkit views render an explicitly fictitious graph fixture.</summary>
internal sealed class VisualizerPreviewView(VisualizerPreviewState state, TerminalCapabilities capabilities) : View
{
    private sealed record Card(PreviewGraphNode Node, View Surface, Label Accent, Label Title, Label Kind, Label Status,
        List<(PreviewGraphMember Member, Label Label)> Members);
    private readonly WorkspaceTheme _theme = new(capabilities.Color);
    private readonly List<Card> _cards = [];
    private readonly List<Label> _links = [];
    private (PreviewZoom Zoom, int Width)? _scene;
    private (Guid Selected, Guid? Member, bool Focused, bool Sidebar)? _presentation;
    public event Action? SelectionChanged;

    public void Update(int width, int height, bool focused, bool sidebar)
    {
        if (width <= 0 || height <= 0) return;
        PreviewGraphLayout layout = new(state.Zoom, width);
        if (_scene != (state.Zoom, width))
        {
            foreach (View child in SubViews.ToArray()) { Remove(child); child.Dispose(); }
            _cards.Clear(); _links.Clear(); _presentation = null;
            _scene = (state.Zoom, width);
            SetContentSize(new Size(layout.Width, layout.Height));
            foreach (PreviewGraphLink link in state.Links)
            {
                PanelBounds from = layout.Bounds(state.Nodes.First(node => node.Id == link.From));
                PanelBounds to = layout.Bounds(state.Nodes.First(node => node.Id == link.To));
                if (from.Y == to.Y)
                {
                    int gap = to.X - from.X - from.Width;
                    AddLink(from.X + from.Width, from.Y + from.Height / 2, gap, 1,
                        new string(capabilities.Unicode ? '─' : '-', Math.Max(0, gap - 1)) + (capabilities.Unicode ? "▶" : ">"));
                    AddLink(from.X + from.Width, from.Y + from.Height / 2 - 1, gap, 1, link.Label);
                }
                else
                {
                    int gap = to.Y - from.Y - from.Height;
                    int x = from.X + from.Width / 2;
                    AddLink(x, from.Y + from.Height, 1, gap,
                        string.Join('\n', Enumerable.Repeat(capabilities.Unicode ? "│" : "|", Math.Max(0, gap - 1)))
                        + "\n" + (capabilities.Unicode ? "▼" : "v"));
                    AddLink(x + 2, from.Y + from.Height + 1, 12, 1, link.Label);
                }
            }
            foreach (PreviewGraphNode node in state.Nodes)
            {
                PanelBounds bounds = layout.Bounds(node);
                View surface = new() { X = bounds.X, Y = bounds.Y, Width = bounds.Width, Height = bounds.Height, CanFocus = false };
                Label accent = MakeLabel(0, 0, bounds.Width, 1, "");
                Label title = MakeLabel(1, 1, bounds.Width - 2, 1, node.Name);
                Label kind = MakeLabel(1, 2, bounds.Width - 2, 1, node.Kind);
                Label status = MakeLabel(1, state.Zoom == PreviewZoom.Overview ? 2 : 3, bounds.Width - 2, 1, StatusLabel(node.Status));
                kind.Visible = state.Zoom != PreviewZoom.Overview;
                surface.Add(accent, title, kind, status);
                foreach (View target in new View[] { surface, accent, title, kind, status })
                {
                    target.MouseBindings.Add(MouseFlags.LeftButtonClicked, Command.Accept);
                    target.Accepting += (_, args) =>
                    {
                        args.Handled = true;
                        state.Select(node.Id);
                        SetFocus();
                        SelectionChanged?.Invoke();
                    };
                }
                List<(PreviewGraphMember Member, Label Label)> members = [];
                for (int i = 0; i < node.Members.Length; i++)
                {
                    PreviewGraphMember member = node.Members[i];
                    Label label = MakeLabel(1, 4 + i, bounds.Width - 2, 1, "  " + member.Name);
                    label.Visible = state.Zoom == PreviewZoom.Details;
                    label.MouseBindings.Add(MouseFlags.LeftButtonClicked, Command.Accept);
                    label.Accepting += (_, args) =>
                    {
                        args.Handled = true;
                        if (state.SelectedId != node.Id) state.Select(node.Id);
                        state.SelectMember(member.Id);
                        SetFocus(); SelectionChanged?.Invoke();
                    };
                    surface.Add(label); members.Add((member, label));
                }
                Add(surface);
                _cards.Add(new(node, surface, accent, title, kind, status, members));
            }
        }
        if (_presentation != (state.SelectedId, state.SelectedMemberId, focused, sidebar))
        {
            _presentation = (state.SelectedId, state.SelectedMemberId, focused, sidebar);
            foreach (Label link in _links) link.SetScheme(_theme.GraphLink(focused, sidebar));
            foreach (Card card in _cards)
            {
                bool selected = card.Node.Id == state.SelectedId;
                Scheme scheme = _theme.GraphCard(selected, focused);
                card.Surface.SetScheme(scheme);
                foreach (View child in card.Surface.SubViews) child.SetScheme(scheme);
                card.Title.SetScheme(_theme.GraphCard(selected, focused, bold: true));
                card.Status.SetScheme(_theme.GraphStatus(card.Node.Status, selected, focused));
                card.Accent.Text = selected ? new string(capabilities.Unicode ? '▔' : '=', layout.CardWidth) : "";
                card.Accent.SetScheme(_theme.GraphStatus(PreviewDesignStatus.ToValidate, selected, focused));
                foreach (var (member, label) in card.Members)
                {
                    bool activeMember = member.Id == state.SelectedMemberId;
                    label.Text = (activeMember ? "> " : "  ") + member.Name;
                    if (activeMember) label.SetScheme(_theme.GraphMember(focused));
                }
            }
        }
        PanelBounds selectedBounds = layout.Bounds(state.Selected);
        if (state.SelectedMemberId is { } memberId)
        {
            int index = Array.FindIndex(state.Selected.Members, member => member.Id == memberId);
            selectedBounds = new(selectedBounds.X + 1, selectedBounds.Y + 4 + index, selectedBounds.Width - 2, 1);
        }
        state.EnsureVisible(selectedBounds, width, height, layout.Width, layout.Height);
        Rectangle viewport = Viewport;
        viewport.Location = new Point(state.ScrollX, state.ScrollY);
        Viewport = viewport;
    }

    private static Label MakeLabel(int x, int y, int width, int height, string text) => new()
    { X = x, Y = y, Width = width, Height = height, Text = text, CanFocus = false };

    private void AddLink(int x, int y, int width, int height, string text)
    {
        Label label = MakeLabel(x, y, width, height, text);
        Add(label); _links.Add(label);
    }

    private static string StatusLabel(PreviewDesignStatus status) => status switch
    {
        PreviewDesignStatus.ToValidate => "? To validate", PreviewDesignStatus.Validated => "+ Validated",
        PreviewDesignStatus.InProgress => "~ In progress", _ => "* Implemented"
    };
}
