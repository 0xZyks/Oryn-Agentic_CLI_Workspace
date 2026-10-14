namespace Oryn.Tui.Prototype;

/// <summary>Lets Kitty's conditional mappings pass reserved shortcuts only while Oryn runs.</summary>
internal sealed class KittyShortcutScope : IDisposable
{
    private readonly bool _enabled = !Console.IsOutputRedirected &&
        (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("KITTY_WINDOW_ID"))
            || (Environment.GetEnvironmentVariable("TERM") ?? "").Contains("kitty", StringComparison.OrdinalIgnoreCase));

    public KittyShortcutScope()
    {
        if (_enabled) Write("\x1b]1337;SetUserVar=oryn=MQ==\x07");
    }

    public void Dispose()
    {
        if (_enabled) Write("\x1b]1337;SetUserVar=oryn\x07");
    }

    private static void Write(string sequence)
    {
        Console.Out.Write(sequence);
        Console.Out.Flush();
    }
}
