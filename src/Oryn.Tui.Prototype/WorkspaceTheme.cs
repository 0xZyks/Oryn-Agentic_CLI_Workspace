using Terminal.Gui.Drawing;
using Oryn.App;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Oryn.Tui.Prototype;

/// <summary>Material-inspired surface and semantic color tokens for the terminal adapter.</summary>
internal sealed class WorkspaceTheme(bool color)
{
    public Scheme Canvas => Surface("#111318", "#e5e1e9");
    public Scheme AppBar => Surface("#23252e", "#e5e1e9");
    public Scheme Caption => Surface("#111318", "#a4a4b6");
    public Scheme Brand => Surface("#23252e", "#cbbaff", bold: true);
    public Scheme Breadcrumb => Surface("#23252e", "#bfc0cf");
    public Scheme FocusAccent => Surface("#111318", "#cbbaff", bold: !color);
    public Scheme TabFocusAccent => Surface(PanelBackground(false, true), "#85769f", faint: !color);
    public Scheme ChatInput(bool focused) => Surface(focused ? "#303341" : "#20222c", focused ? "#e5e1e9" : "#aaa6b3");
    public Scheme CodeCanvas(bool focused) => Surface(focused ? "#151820" : "#0f1117", focused ? "#e5e1e9" : "#aaa6b3");
    public Scheme GraphLink(bool focused, bool sidebar) => Surface(PanelBackground(sidebar, focused), focused ? "#85769f" : "#555162");
    public Scheme GraphMember(bool focused) => Surface(focused ? "#51415f" : "#30283a", focused ? "#e9ddff" : "#aaa6b3", bold: focused);
    public Scheme GraphCard(bool selected, bool focused, bool bold = false)
    {
        if (!color && selected && focused)
        {
            GuiAttribute inverse = new(new Color("black"), new Color("white"), bold ? TextStyle.Bold : TextStyle.None);
            return new(inverse) { Focus = inverse, Active = inverse, ReadOnly = inverse };
        }
        return Surface(GraphBackground(selected, focused), focused ? "#e5e1e9" : "#aaa6b3", bold);
    }
    public Scheme GraphStatus(PreviewDesignStatus status, bool selected, bool focused) => !color
        ? GraphCard(selected, focused)
        : Surface(GraphBackground(selected, focused), !focused ? "#858494" : status switch
        {
            PreviewDesignStatus.Implemented => "#9fc5ad", PreviewDesignStatus.InProgress => "#d2bc8d",
            PreviewDesignStatus.Validated => "#9ebbd3", _ => "#cbbaff"
        });
    private static string GraphBackground(bool selected, bool focused) => selected
        ? focused ? "#39304d" : "#24212c" : focused ? "#282c37" : "#1c1f28";
    public Scheme ChatPromptMarker(bool focused) => Surface(focused ? "#303341" : "#20222c", focused ? "#85769f" : "#555162");
    public Scheme Panel(bool sidebar, bool focused) => PanelText(sidebar, focused);
    public Scheme PanelText(bool sidebar, bool focused, bool muted = false, bool bold = false) =>
        Surface(PanelBackground(sidebar, focused), muted
            ? focused ? "#bfc0cf" : "#858494"
            : focused ? "#e5e1e9" : "#aaa6b3", bold);
    public Scheme Heading(bool focused, bool content = false) =>
        Surface(PanelBackground(!content, focused), focused ? "#cbbaff" : "#8f8d9d", bold: focused);
    public Scheme Tab(bool selected, bool focused = true, bool sidebar = false) => Surface(
        selected ? focused ? "#39304d" : "#24212c" : PanelBackground(sidebar, focused),
        selected ? focused ? "#e9ddff" : "#a69ab9" : focused ? "#bfc0cf" : "#858494", bold: selected && focused);

    public Scheme Explorer(bool focused)
    {
        GuiAttribute normal = Attribute(focused ? "#e5e1e9" : "#aaa6b3", PanelBackground(true, focused));
        GuiAttribute selected = color
            ? Attribute(focused ? "#e9ddff" : "#b1a7c5", focused ? "#39304d" : "#25212f")
            : new(new Color("black"), new Color("white"));
        return new(normal)
        {
            Focus = selected, HotFocus = selected, Active = selected, HotActive = selected,
            Highlight = selected, ReadOnly = normal
        };
    }

    private static string PanelBackground(bool sidebar, bool focused) => sidebar
        ? focused ? "#242630" : "#171920"
        : focused ? "#20232c" : "#13151b";

    private Scheme Surface(string background, string foreground, bool bold = false, bool faint = false)
    {
        GuiAttribute normal = Attribute(foreground, background, bold, faint);
        return new(normal)
        {
            Focus = normal, HotFocus = normal, HotNormal = normal,
            Active = normal, HotActive = normal, ReadOnly = normal, Editable = normal,
            Highlight = normal, Disabled = normal
        };
    }

    private GuiAttribute Attribute(string foreground, string background, bool bold = false, bool faint = false) =>
        new(new Color(color ? foreground : "white"), new Color(color ? background : "black"),
            (bold ? TextStyle.Bold : TextStyle.None) | (faint ? TextStyle.Faint : TextStyle.None));
}
