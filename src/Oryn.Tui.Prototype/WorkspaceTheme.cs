using Terminal.Gui.Drawing;
using GuiAttribute = Terminal.Gui.Drawing.Attribute;

namespace Oryn.Tui.Prototype;

/// <summary>Material-inspired surface and semantic color tokens for the terminal adapter.</summary>
internal sealed class WorkspaceTheme(bool color)
{
    public Scheme Canvas => Surface("#111318", "#e5e1e9");
    public Scheme AppBar => Surface("#23252e", "#e5e1e9");
    public Scheme Sidebar => Surface("#1c1e26", "#e5e1e9");
    public Scheme Content => Surface("#171920", "#e5e1e9");
    public Scheme SidebarMuted => Surface("#1c1e26", "#a4a4b6");
    public Scheme ContentMuted => Surface("#171920", "#a4a4b6");
    public Scheme WelcomeHeading => Surface("#171920", "#e5e1e9", bold: true);
    public Scheme MapHeading => Surface("#1c1e26", "#e5e1e9", bold: true);
    public Scheme Caption => Surface("#111318", "#a4a4b6");
    public Scheme Brand => Surface("#23252e", "#cbbaff", bold: true);
    public Scheme Breadcrumb => Surface("#23252e", "#bfc0cf");
    public Scheme Heading(bool focused) => Surface("#1c1e26", focused ? "#cbbaff" : "#bfc0cf", bold: true);
    public Scheme Tab(bool selected) => Surface(selected ? "#39304d" : "#171920", selected ? "#e9ddff" : "#a4a4b6", bold: selected);

    public Scheme Explorer
    {
        get
        {
            GuiAttribute normal = Attribute("#e5e1e9", "#1c1e26");
            GuiAttribute selected = color ? Attribute("#e9ddff", "#39304d") : new(new Color("black"), new Color("white"));
            return new(normal)
            {
                Focus = selected, HotFocus = selected, Active = selected, HotActive = selected,
                Highlight = selected, ReadOnly = normal
            };
        }
    }

    private Scheme Surface(string background, string foreground, bool bold = false)
    {
        GuiAttribute normal = Attribute(foreground, background, bold);
        return new(normal)
        {
            Focus = normal, HotFocus = normal, HotNormal = normal,
            Active = normal, HotActive = normal, ReadOnly = normal,
            Highlight = normal, Disabled = normal
        };
    }

    private GuiAttribute Attribute(string foreground, string background, bool bold = false) =>
        new(new Color(color ? foreground : "white"), new Color(color ? background : "black"),
            bold ? TextStyle.Bold : TextStyle.None);
}
