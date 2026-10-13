namespace Oryn.App;

public sealed record TerminalCapabilities(bool Interactive, bool Color, bool Unicode, string Description)
{
    public static TerminalCapabilities Detect(WorkspaceSettings settings)
    {
        string? term = Environment.GetEnvironmentVariable("TERM");
        bool interactive = !Console.IsInputRedirected && !Console.IsOutputRedirected && term != "dumb";
        bool color = settings.UseColor && Environment.GetEnvironmentVariable("NO_COLOR") is null;
        bool unicode = settings.UseUnicode && Console.OutputEncoding.CodePage == 65001;
        return new(interactive, color, unicode, $"{term ?? "console"} / {(color ? "color" : "mono")} / {(unicode ? "Unicode" : "ASCII")}");
    }
}
