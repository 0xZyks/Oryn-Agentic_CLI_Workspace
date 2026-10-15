using System.Text;
using System.Text.Json;
using Oryn.App;
using Oryn.Ui.Sdl;

Console.OutputEncoding = Encoding.UTF8;
string workingDirectory = Directory.GetCurrentDirectory();

try
{
    StartupOptions options = StartupOptions.Parse(args, workingDirectory);
    if (options.ShowHelp)
    {
        Console.WriteLine("""
            Oryn — local-first graphical engineering workspace (M0)

            Usage: oryn [directory] [options]
            Without a directory, opens the caller's current working directory.

            --help, -h       Show help
            --version        Show version
            --check          Check project access and report JSON without opening a window
            --config <file>  Read explicit JSON settings
            --no-color       Use a monochrome palette (also respects NO_COLOR)
            --ascii          Use ASCII UI decorations
            --fps auto|N     Follow the window's monitor, or set 1–500 Hz manually
            --keybindings <file> Use a custom grouped keybindings file
            --               Treat remaining argument as a directory

            Tab              Move within the active panel
            Shift+Tab / F9   Select the application menu bar
            Ctrl+Tab         Switch panels (Oryn Super+Tab)
            Ctrl+Enter / F6  Select central tabs (or confirm when already there)
            Enter in tabs    Confirm the selected tab and focus its content
            Ctrl+K           Edit grouped keybindings (Super defaults to Ctrl)
            Ctrl+F           Search files by name or text in the explorer
            Settings menu    Appearance / colors: preview, apply or save
            Up / Down        Select or scroll
            Enter            Open file or toggle folder
            Right            Expand folder or select its first child
            Left             Collapse folder
            F1 / F2 / F3 / F4 Sources / Chat / Source preview / Visualiser
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
    string keybindingsPath = options.KeybindingsPath ?? KeyBindingStore.DefaultPath;
    KeyboardRouter keyboard = new(KeyBindingStore.Load(keybindingsPath));
    RenderCadence cadence = new(options.OverrideRenderCadence ? options.RenderFramesPerSecond : settings.RenderFramesPerSecond);
    LocalWorkspaceFiles files = new(options.ProjectRoot, settings);
    // SDL window creation stays on the process main thread, including on macOS.
    DirectoryResult directory = files.ListAsync(options.ProjectRoot, CancellationToken.None).GetAwaiter().GetResult();
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
            frontend = new { window = "SDL3", drawing = "SkiaSharp", style = "architecture-workshop" },
            renderCadence = new { manualFramesPerSecond = cadence.ManualFramesPerSecond, auto = cadence.ManualFramesPerSecond is null },
            keybindings = new { path = keybindingsPath, keyboard.Settings.Super }
        }));
        return 0;
    }
    WorkspaceState state = new(options.ProjectRoot);
    state.LoadRoot(directory);
    state.Dispatch(WorkspaceCommand.ChatTab);
    state.Graph.ChangeZoom(-1);
    using GraphicalWorkspaceController controller = new(state, files, keyboard, keybindingsPath);
    using SdlWorkspace window = new(controller, settings.UseColor, settings.UseUnicode, cadence.ManualFramesPerSecond);
    window.Run();
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
    // Native window resources are released before reporting errors.
    Console.Error.WriteLine($"oryn: window startup or rendering failed: {TerminalText.Sanitize(ex.Message)}");
    if (Environment.GetEnvironmentVariable("ORYN_DEBUG") == "1") Console.Error.WriteLine(ex);
    return 1;
}
