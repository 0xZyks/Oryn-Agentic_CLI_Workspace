using System.Text.Json;

namespace Oryn.App;

public enum KeyBindingGroup { Workspace, Panels, Tabs, Explorer, Content, Graph, Text, Dialog, Navbar }
public enum KeyboardAction
{
    Quit, OpenMenu, EditKeybindings, ToggleDock, DockLeft, DockRight, ResizeSmaller, ResizeLarger, ResetSizes, CancelResize,
    NextPanel, PreviousPanel, FocusFiles, FocusChat, FocusCode, FocusVisualizer,
    ToggleTabFocus, NextTab, PreviousTab, ConfirmTab, ReturnToContent,
    MoveUp, MoveDown, OpenSelection, ExpandSelection, Collapse, ContentUp, ContentDown, ContentLeft, ContentRight,
    GraphZoomIn, GraphZoomOut, GraphResetZoom, GraphEnterComponent, GraphExitComponent, GraphOptions,
    TextSubmit, TextNewline, TextBackspace, TextDelete, TextLeft, TextRight, TextUp, TextDown, TextHome, TextEnd,
    TextSelectLeft, TextSelectRight, TextSelectUp, TextSelectDown, TextSelectHome, TextSelectEnd, TextSelectAll, TextCopy, TextCut, TextPaste,
    ToggleHistory, DialogClose, DialogNext, DialogPrevious, DialogConfirm, BindingNext, BindingPrevious, BindingApply, BindingSave,
    ToggleNavbarFocus, NextNavbar, PreviousNavbar, ConfirmNavbar, LeaveNavbar
}
[Flags]
public enum InputModifiers { None = 0, Ctrl = 1, Alt = 2, Shift = 4, AltGraph = 8, System = 16 }
public readonly record struct InputKey(string Name, InputModifiers Modifiers = InputModifiers.None)
{
    public static InputKey Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64) throw new ArgumentException("A binding needs a key name.");
        string[] parts = value.Split('+', StringSplitOptions.TrimEntries);
        InputModifiers modifiers = InputModifiers.None;
        foreach (string part in parts[..^1])
            modifiers |= part.ToUpperInvariant() switch
            {
                "CTRL" => InputModifiers.Ctrl, "ALT" => InputModifiers.Alt, "SHIFT" => InputModifiers.Shift,
                _ => throw new ArgumentException($"Unknown modifier: {part}.")
            };
        string name = parts[^1].ToUpperInvariant();
        name = name switch { "ESCAPE" => "ESC", "RETURN" => "ENTER", _ => name };
        bool known = name.Length == 1 && char.IsAsciiLetterOrDigit(name[0])
            || name is "TAB" or "ENTER" or "ESC" or "SPACE" or "UP" or "DOWN" or "LEFT" or "RIGHT"
                or "HOME" or "END" or "PAGEUP" or "PAGEDOWN" or "BACKSPACE" or "DELETE" or "INSERT" or "PLUS" or "MINUS"
            || name.StartsWith('F') && int.TryParse(name[1..], out int function) && function is >= 1 and <= 24;
        if (!known) throw new ArgumentException($"Unknown key: {parts[^1]}.");
        return new(name, modifiers);
    }
}

public sealed record KeyBindingDefinition(KeyBindingGroup Group, KeyboardAction Action, string[] Defaults);

public sealed record KeyBindingSettings
{
    public string Super { get; init; } = "Ctrl";
    public int LeaderTimeoutMilliseconds { get; init; } = 1000;
    public Dictionary<string, string[]> Workspace { get; init; } = [];
    public Dictionary<string, string[]> Panels { get; init; } = [];
    public Dictionary<string, string[]> Tabs { get; init; } = [];
    public Dictionary<string, string[]> Explorer { get; init; } = [];
    public Dictionary<string, string[]> Content { get; init; } = [];
    public Dictionary<string, string[]> Graph { get; init; } = [];
    public Dictionary<string, string[]> Text { get; init; } = [];
    public Dictionary<string, string[]> Dialog { get; init; } = [];
    public Dictionary<string, string[]> Navbar { get; init; } = [];

    public Dictionary<string, string[]> Group(KeyBindingGroup group) => group switch
    {
        KeyBindingGroup.Workspace => Workspace, KeyBindingGroup.Panels => Panels, KeyBindingGroup.Tabs => Tabs,
        KeyBindingGroup.Explorer => Explorer, KeyBindingGroup.Graph => Graph, KeyBindingGroup.Text => Text,
        KeyBindingGroup.Dialog => Dialog, KeyBindingGroup.Navbar => Navbar, _ => Content
    };
    public string[] Bindings(KeyBindingDefinition definition) => Group(definition.Group).TryGetValue(definition.Action.ToString(), out string[]? keys)
        ? keys : definition.Defaults;
    public KeyBindingSettings DeepCopy() => JsonSerializer.Deserialize<KeyBindingSettings>(JsonSerializer.Serialize(this))!;
    public static string Label(KeyboardAction action) =>
        System.Text.RegularExpressions.Regex.Replace(action.ToString(), "(?<!^)([A-Z])", " $1");

    public static IReadOnlyList<KeyBindingDefinition> Definitions { get; } =
    [
        new(KeyBindingGroup.Workspace, KeyboardAction.Quit, ["Super+Q"]),
        new(KeyBindingGroup.Workspace, KeyboardAction.OpenMenu, ["F10"]),
        new(KeyBindingGroup.Navbar, KeyboardAction.ToggleNavbarFocus, ["Shift+Tab", "F9"]),
        new(KeyBindingGroup.Navbar, KeyboardAction.NextNavbar, ["Right", "Tab"]),
        new(KeyBindingGroup.Navbar, KeyboardAction.PreviousNavbar, ["Left"]),
        new(KeyBindingGroup.Navbar, KeyboardAction.ConfirmNavbar, ["Enter"]),
        new(KeyBindingGroup.Navbar, KeyboardAction.LeaveNavbar, ["Esc"]),
        new(KeyBindingGroup.Workspace, KeyboardAction.EditKeybindings, ["Super+K"]),
        new(KeyBindingGroup.Workspace, KeyboardAction.ToggleDock, ["Super+D"]),
        new(KeyBindingGroup.Workspace, KeyboardAction.DockLeft, ["Super+Shift+Left"]),
        new(KeyBindingGroup.Workspace, KeyboardAction.DockRight, ["Super+Shift+Right"]),
        new(KeyBindingGroup.Workspace, KeyboardAction.ResizeSmaller, ["Super+Left"]),
        new(KeyBindingGroup.Workspace, KeyboardAction.ResizeLarger, ["Super+Right"]),
        new(KeyBindingGroup.Workspace, KeyboardAction.ResetSizes, ["F8"]),
        new(KeyBindingGroup.Workspace, KeyboardAction.CancelResize, ["Esc"]),
        new(KeyBindingGroup.Panels, KeyboardAction.NextPanel, ["Super+Tab"]),
        new(KeyBindingGroup.Panels, KeyboardAction.PreviousPanel, ["Super+Shift+Tab"]),
        new(KeyBindingGroup.Panels, KeyboardAction.FocusFiles, ["F1"]),
        new(KeyBindingGroup.Panels, KeyboardAction.FocusChat, ["F2"]),
        new(KeyBindingGroup.Panels, KeyboardAction.FocusCode, ["F3"]),
        new(KeyBindingGroup.Panels, KeyboardAction.FocusVisualizer, ["F4"]),
        new(KeyBindingGroup.Tabs, KeyboardAction.ToggleTabFocus, ["Super+Enter", "F6"]),
        new(KeyBindingGroup.Tabs, KeyboardAction.NextTab, ["Right", "Tab"]),
        new(KeyBindingGroup.Tabs, KeyboardAction.PreviousTab, ["Left"]),
        new(KeyBindingGroup.Tabs, KeyboardAction.ConfirmTab, ["Enter"]),
        new(KeyBindingGroup.Tabs, KeyboardAction.ReturnToContent, ["Esc"]),
        new(KeyBindingGroup.Explorer, KeyboardAction.MoveUp, ["Up"]),
        new(KeyBindingGroup.Explorer, KeyboardAction.MoveDown, ["Down"]),
        new(KeyBindingGroup.Explorer, KeyboardAction.OpenSelection, ["Enter"]),
        new(KeyBindingGroup.Explorer, KeyboardAction.ExpandSelection, ["Right"]),
        new(KeyBindingGroup.Explorer, KeyboardAction.Collapse, ["Left"]),
        new(KeyBindingGroup.Content, KeyboardAction.ContentUp, ["Up"]),
        new(KeyBindingGroup.Content, KeyboardAction.ContentDown, ["Down"]),
        new(KeyBindingGroup.Content, KeyboardAction.ContentLeft, ["Left"]),
        new(KeyBindingGroup.Content, KeyboardAction.ContentRight, ["Right"]),
        new(KeyBindingGroup.Graph, KeyboardAction.GraphZoomIn, ["PageUp", "Plus", "Shift+Plus"]),
        new(KeyBindingGroup.Graph, KeyboardAction.GraphZoomOut, ["PageDown", "Minus"]),
        new(KeyBindingGroup.Graph, KeyboardAction.GraphResetZoom, ["Home"]),
        new(KeyBindingGroup.Graph, KeyboardAction.GraphEnterComponent, ["Enter"]),
        new(KeyBindingGroup.Graph, KeyboardAction.GraphExitComponent, ["Esc"]),
        new(KeyBindingGroup.Graph, KeyboardAction.GraphOptions, ["Alt+Enter"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextSubmit, ["Enter"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextNewline, ["Shift+Enter"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextBackspace, ["Backspace"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextDelete, ["Delete"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextLeft, ["Left"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextRight, ["Right"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextUp, ["Up"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextDown, ["Down"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextHome, ["Home"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextEnd, ["End"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextSelectLeft, ["Shift+Left"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextSelectRight, ["Shift+Right"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextSelectUp, ["Shift+Up"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextSelectDown, ["Shift+Down"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextSelectHome, ["Shift+Home"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextSelectEnd, ["Shift+End"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextSelectAll, ["Ctrl+A"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextCopy, ["Ctrl+C"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextCut, ["Ctrl+X"]),
        new(KeyBindingGroup.Text, KeyboardAction.TextPaste, ["Ctrl+V"]),
        new(KeyBindingGroup.Content, KeyboardAction.ToggleHistory, ["Tab"]),
        new(KeyBindingGroup.Dialog, KeyboardAction.DialogClose, ["Esc"]),
        new(KeyBindingGroup.Dialog, KeyboardAction.DialogNext, ["Down", "Tab"]),
        new(KeyBindingGroup.Dialog, KeyboardAction.DialogPrevious, ["Up", "Shift+Tab"]),
        new(KeyBindingGroup.Dialog, KeyboardAction.DialogConfirm, ["Enter"]),
        new(KeyBindingGroup.Dialog, KeyboardAction.BindingNext, ["Tab"]),
        new(KeyBindingGroup.Dialog, KeyboardAction.BindingPrevious, ["Shift+Tab"]),
        new(KeyBindingGroup.Dialog, KeyboardAction.BindingApply, ["Alt+A"]),
        new(KeyBindingGroup.Dialog, KeyboardAction.BindingSave, ["Alt+S"])
    ];
}

public static class KeyBindingStore
{
    public static string DefaultPath
    {
        get
        {
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(root))
            {
                string user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrEmpty(user)) throw new IOException("User settings directory unavailable. Use --keybindings with an explicit path.");
                root = Path.Combine(user, ".config");
            }
            return Path.Combine(root, "oryn", "keybindings.json");
        }
    }
    public static KeyBindingSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Keybindings exceed 64 KiB.");
        KeyBindingSettings settings = JsonSerializer.Deserialize<KeyBindingSettings>(File.ReadAllText(path), new JsonSerializerOptions
            { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Keybindings must be a JSON object.");
        // Saved copies of the former defaults should follow the new defaults,
        // without rewriting the user's file or changing deliberate remappings.
        if (settings.Panels.TryGetValue("NextPanel", out string[]? next) && next is not null
            && next.SequenceEqual(new[] { "Super+Tab", "Tab" }, StringComparer.OrdinalIgnoreCase))
            settings.Panels["NextPanel"] = ["Super+Tab"];
        if (settings.Panels.TryGetValue("PreviousPanel", out string[]? previous) && previous is not null
            && previous.SequenceEqual(new[] { "Super+Shift+Tab", "Shift+Tab" }, StringComparer.OrdinalIgnoreCase))
            settings.Panels["PreviousPanel"] = ["Super+Shift+Tab"];
        if (settings.Workspace.TryGetValue("Quit", out string[]? quit) && quit is not null
            && quit.SequenceEqual(new[] { "Super+Q", "Ctrl+C" }, StringComparer.OrdinalIgnoreCase))
            settings.Workspace["Quit"] = ["Super+Q"];
        if (settings.Explorer.TryGetValue("OpenSelection", out string[]? open) && open is not null
            && open.SequenceEqual(new[] { "Enter", "Right" }, StringComparer.OrdinalIgnoreCase))
            settings.Explorer["OpenSelection"] = ["Enter"];
        // Release Shift+Tab from exact former local defaults; preserve explicit custom mappings.
        void Migrate(Dictionary<string, string[]> group, string action, string[] oldKeys, string[] newKeys)
        {
            if (group.TryGetValue(action, out var keys) && keys is not null && keys.SequenceEqual(oldKeys, StringComparer.OrdinalIgnoreCase))
                group[action] = newKeys;
        }
        Migrate(settings.Navbar, "ToggleNavbarFocus", ["Alt+Tab", "F9"], ["Shift+Tab", "F9"]);
        Migrate(settings.Navbar, "PreviousNavbar", ["Left", "Shift+Tab"], ["Left"]);
        Migrate(settings.Tabs, "PreviousTab", ["Left", "Shift+Tab"], ["Left"]);
        Migrate(settings.Content, "ToggleHistory", ["Tab", "Shift+Tab"], ["Tab"]);
        _ = new KeyboardRouter(settings);
        return settings;
    }
    public static void Save(string path, KeyBindingSettings settings)
    {
        _ = new KeyboardRouter(settings);
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        if (System.Text.Encoding.UTF8.GetByteCount(json) > 65536) throw new InvalidDataException("Keybindings exceed 64 KiB.");
        string fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temporary, json); File.Move(temporary, fullPath, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
