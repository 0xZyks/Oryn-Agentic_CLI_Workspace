using System.Text;
using System.Text.Json;
using Oryn.App;
using Oryn.Tui.Prototype;

Console.OutputEncoding = Encoding.UTF8;
string workingDirectory = Directory.GetCurrentDirectory();

try
{
    StartupOptions options = StartupOptions.Parse(args, workingDirectory);
    if (options.ShowHelp)
    {
        Console.WriteLine("""
            Oryn — local-first terminal engineering workspace (M0)

            Usage: oryn [directory] [options]
            Without a directory, opens the caller's current working directory.

            --help, -h       Show help
            --version        Show version
            --check          Check project access and report JSON without opening a TUI
            --config <file>  Read explicit JSON settings
            --no-color       Use a monochrome palette (also respects NO_COLOR)
            --ascii          Use ASCII UI decorations
            --fps auto|N     Follow the terminal's monitor, or set 1–500 Hz manually
            --keybindings <file> Use a custom grouped keybindings file
            --               Treat remaining argument as a directory

            Tab / Shift+Tab  Move within the active panel
            Ctrl+Tab         Switch panels (Oryn Super+Tab)
            Ctrl+Enter / F6  Select central tabs (or confirm when already there)
            Enter in tabs    Confirm the selected tab and focus its content
            Ctrl+K           Edit grouped keybindings (Super defaults to Ctrl)
            Up / Down        Select or scroll
            Enter / Right    Open file or expand folder
            Left             Collapse folder
            F1 / F2 / F3 / F4 Files / Chat / Code / Visualizer
            Ctrl+D           Toggle visualizer tab / dock
            Ctrl+Shift+Left  Dock visualizer left
            Ctrl+Shift+Right Dock visualizer right
            Ctrl+Left/Right  Shrink / grow focused panel
            F8               Reset panel sizes
            F10              Open menu
            Ctrl+Q           Quit

            Drag the separators between panels to resize; Escape cancels a drag.
            """);
        return 0;
    }
    if (options.ShowVersion) { Console.WriteLine("oryn 0.1.0-m0"); return 0; }
    if (!Directory.Exists(options.ProjectRoot))
        throw new DirectoryNotFoundException($"Project directory does not exist or cannot be accessed: {options.ProjectRoot}");

    WorkspaceSettings settings = WorkspaceSettings.Load(options.ConfigPath);
    settings = settings with { UseColor = settings.UseColor && options.UseColor, UseUnicode = settings.UseUnicode && options.UseUnicode };
    TerminalCapabilities capabilities = TerminalCapabilities.Detect(settings);
    string keybindingsPath = options.KeybindingsPath ?? KeyBindingStore.DefaultPath;
    KeyboardRouter keyboard = new(KeyBindingStore.Load(keybindingsPath));
    RenderCadence cadence = new(options.OverrideRenderCadence ? options.RenderFramesPerSecond : settings.RenderFramesPerSecond);
    IDisplayRefreshRateProbe refreshRateProbe = NiriDisplayRefreshRateProbe.ForCurrentTerminal();
    if (cadence.ManualFramesPerSecond is null)
        cadence.Apply(await refreshRateProbe.ReadAsync(CancellationToken.None));
    LocalWorkspaceFiles files = new(options.ProjectRoot, settings);
    DirectoryResult directory = await files.ListAsync(options.ProjectRoot, CancellationToken.None);
    if (directory.Error is not null) throw new IOException(directory.Error);
    if (options.Check)
    {
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            projectRoot = options.ProjectRoot,
            workingDirectory,
            entryCount = directory.Entries.Count,
            truncated = directory.Truncated,
            warning = directory.Warning,
            terminal = capabilities,
            renderCadence = new { cadence.FramesPerSecond, cadence.ManualFramesPerSecond, cadence.Reading },
            keybindings = new { path = keybindingsPath, keyboard.Settings.Super }
        }));
        return 0;
    }
    if (!capabilities.Interactive)
    {
        Console.Error.WriteLine("Oryn needs an interactive terminal. Use --check for a non-interactive project check.");
        return 2;
    }

    WorkspaceState state = new(options.ProjectRoot);
    state.LoadRoot(directory);
    new TerminalWorkspace(state, files, capabilities, cadence, refreshRateProbe, keyboard, keybindingsPath).Run();
    return 0;
}
catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException)
{
    Console.Error.WriteLine($"oryn: {TerminalText.Sanitize(ex.Message)}");
    if (Environment.GetEnvironmentVariable("ORYN_DEBUG") == "1") Console.Error.WriteLine(ex);
    return 1;
}
catch (Exception ex)
{
    // The TUI's using/finally cleanup runs before printing errors to the shell.
    Console.Error.WriteLine($"oryn: terminal startup or rendering failed: {TerminalText.Sanitize(ex.Message)}");
    if (Environment.GetEnvironmentVariable("ORYN_DEBUG") == "1") Console.Error.WriteLine(ex);
    return 1;
}
