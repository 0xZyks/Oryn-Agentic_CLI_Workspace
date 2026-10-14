namespace Oryn.App;

public enum KeyboardScope { Explorer, Tabs, TextEditor, History, Content, Graph, Menu, Details, ElementChat, Bindings, Navbar }

public sealed record KeyboardContext(WorkspacePanel Panel, PanelFocusArea Area, bool Resizing = false,
    bool ContentAcceptsTab = false, bool ContentAcceptsText = false, bool GraphActive = false, KeyboardScope? Scope = null);
public readonly record struct KeyRoutingResult(KeyboardAction? Action, bool Handled, bool Repeatable = false);

/// <summary>Framework-independent grouped shortcuts with contextual scopes and an optional leader.</summary>
public sealed class KeyboardRouter
{
    private sealed record Binding(KeyBindingDefinition Definition, InputKey Key, bool Leader);
    private readonly List<Binding> _bindings = [];
    private readonly Dictionary<(KeyboardScope Scope, bool Resize, InputKey Key, bool Leader), KeyboardAction> _commands = [];
    private readonly InputKey? _leader;
    private readonly int _timeout;
    private long? _pendingUntil;
    public KeyBindingSettings Settings { get; }
    public bool LeaderPending => _pendingUntil is not null;

    public KeyboardRouter(KeyBindingSettings settings)
    {
        Settings = settings.DeepCopy();
        _timeout = settings.LeaderTimeoutMilliseconds;
        if (_timeout is < 100 or > 5000) throw new ArgumentException("Leader timeout must be from 100 to 5000 ms.");
        InputModifiers super = InputModifiers.None;
        string superName = settings.Super?.Trim() ?? "";
        if (superName.Equals("Win", StringComparison.OrdinalIgnoreCase) || superName.Equals("Command", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Win/Command is not exposed by this terminal adapter. Choose Ctrl/Alt or a leader key remapped by your terminal.");
        if (superName.Split('+').All(part => part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
            || part.Equals("Alt", StringComparison.OrdinalIgnoreCase) || part.Equals("Shift", StringComparison.OrdinalIgnoreCase)))
            super = InputKey.Parse(superName + "+Tab").Modifiers;
        else _leader = InputKey.Parse(superName);

        foreach (KeyBindingGroup group in Enum.GetValues<KeyBindingGroup>())
        {
            Dictionary<string, string[]> values = settings.Group(group) ?? throw new ArgumentException($"Missing {group} group.");
            foreach (string action in values.Keys)
                if (!KeyBindingSettings.Definitions.Any(definition => definition.Group == group && definition.Action.ToString() == action))
                    throw new ArgumentException($"Unknown {group} action: {action}.");
        }
        foreach (KeyBindingDefinition definition in KeyBindingSettings.Definitions)
        {
            string[] keys = settings.Bindings(definition) ?? throw new ArgumentException($"Missing bindings for {definition.Action}.");
            if (keys.Length > 4) throw new ArgumentException("Use at most four shortcuts per action.");
            if (keys.Length == 0 && definition.Action is KeyboardAction.Quit or KeyboardAction.EditKeybindings or KeyboardAction.NextPanel
                or KeyboardAction.PreviousPanel or KeyboardAction.ToggleTabFocus)
                throw new ArgumentException($"Keep at least one shortcut for {definition.Action}.");
            foreach (string chord in keys)
            {
                if (chord is null) throw new ArgumentException("A shortcut cannot be null.");
                string[] tokens = chord.Split('+', StringSplitOptions.TrimEntries);
                bool usesSuper = tokens.Any(token => token.Equals("Super", StringComparison.OrdinalIgnoreCase));
                string concrete = string.Join('+', tokens.Where(token => !token.Equals("Super", StringComparison.OrdinalIgnoreCase)));
                InputKey key = InputKey.Parse(concrete);
                if (usesSuper && _leader is null) key = key with { Modifiers = key.Modifiers | super };
                // Keep Alt-Super default panel access away from desktop-reserved Alt+Tab.
                if (_leader is null && super == InputModifiers.Alt && usesSuper
                    && definition.Action is KeyboardAction.NextPanel or KeyboardAction.PreviousPanel
                    && keys.SequenceEqual(definition.Defaults, StringComparer.OrdinalIgnoreCase))
                    key = key with { Modifiers = (key.Modifiers & ~InputModifiers.Alt) | InputModifiers.Ctrl };
                Binding binding = new(definition, key, usesSuper && _leader is not null);
                if (_leader is { } leader && key == leader && !binding.Leader)
                    throw new ArgumentException($"{chord} is reserved as the Super leader.");
                _bindings.Add(binding);
            }
        }
        // Build a single dispatch table for each scope. Validation and dispatch use the same table.
        foreach (KeyboardScope scope in Enum.GetValues<KeyboardScope>())
        foreach (bool resize in new[] { false, true })
        foreach (Binding binding in _bindings)
        {
            KeyboardAction action = binding.Definition.Action;
            if (!Applies(action, scope, resize)) continue;
            var slot = (scope, resize, binding.Key, binding.Leader);
            if (_commands.TryGetValue(slot, out KeyboardAction other))
            {
                if (other == action) continue;
                // Escape cancels an active splitter operation before the underlying context.
                if (resize && (action == KeyboardAction.CancelResize || other == KeyboardAction.CancelResize))
                { _commands[slot] = KeyboardAction.CancelResize; continue; }
                // With Alt as Super, Alt+Enter still opens graph options in the graph scope.
                if (scope == KeyboardScope.Graph && binding.Key == InputKey.Parse("Alt+Enter")
                    && (action == KeyboardAction.GraphOptions && other == KeyboardAction.ToggleTabFocus
                        || other == KeyboardAction.GraphOptions && action == KeyboardAction.ToggleTabFocus))
                { _commands[slot] = KeyboardAction.GraphOptions; continue; }
                throw new ArgumentException($"Shortcut conflict in {scope}: {binding.Key} ({action} / {other}).");
            }
            _commands.Add(slot, action);
        }
    }

    public bool Expire(long now)
    {
        if (_pendingUntil is null || now < _pendingUntil) return false;
        _pendingUntil = null;
        return true;
    }
    public void CancelLeader() => _pendingUntil = null;
    public bool IsLeaderKey(InputKey key) => _leader == key;
    public string Hint(KeyboardAction action)
    {
        KeyBindingDefinition definition = KeyBindingSettings.Definitions.First(definition => definition.Action == action);
        return string.Join(" / ", Settings.Bindings(definition));
    }

    public string DisplayHint(KeyboardAction action) => string.Join(" / ",
        _bindings.Where(binding => binding.Definition.Action == action).Select(binding =>
        {
            string chord = (binding.Key.Modifiers.HasFlag(InputModifiers.Ctrl) ? "Ctrl+" : "")
                + (binding.Key.Modifiers.HasFlag(InputModifiers.Alt) ? "Alt+" : "")
                + (binding.Key.Modifiers.HasFlag(InputModifiers.Shift) ? "Shift+" : "")
                + (binding.Key.Name.StartsWith('F') || binding.Key.Name.Length == 1 ? binding.Key.Name
                    : binding.Key.Name[0] + binding.Key.Name[1..].ToLowerInvariant());
            return binding.Leader ? Settings.Super + " then " + chord : chord;
        }));

    public KeyRoutingResult Route(InputKey key, KeyboardContext context, long now, bool repeat = false)
    {
        Expire(now);
        KeyboardScope scope = context.Scope ?? (context.Area == PanelFocusArea.Tabs ? KeyboardScope.Tabs
            : context.Panel == WorkspacePanel.Explorer ? KeyboardScope.Explorer
            : context.GraphActive ? KeyboardScope.Graph
            : context.ContentAcceptsText ? KeyboardScope.TextEditor : KeyboardScope.Content);
        bool text = scope is KeyboardScope.TextEditor or KeyboardScope.ElementChat or KeyboardScope.Bindings;
        bool textLeader = _leader is { Modifiers: InputModifiers.None } prefix && (prefix.Name == "SPACE" || prefix.Name.Length == 1);
        if (text && textLeader) CancelLeader();
        if (_leader == key && !(text && textLeader))
        { if (!repeat) _pendingUntil = now + _timeout; return new(null, true); }
        if (repeat && LeaderPending) return new(null, true);
        bool leader = LeaderPending;
        _pendingUntil = null;
        if (leader && key == new InputKey("ESC")) return new(null, true);
        if (text && !leader && key.Modifiers is InputModifiers.None or InputModifiers.Shift
            && (key.Name.Length == 1 || key.Name is "SPACE" or "PLUS" or "MINUS")) return default;
        if (context.ContentAcceptsTab && scope == KeyboardScope.TextEditor && key.Name == "TAB"
            && key.Modifiers == InputModifiers.None && !leader) return default;
        if (_commands.TryGetValue((scope, context.Resizing, key, leader), out KeyboardAction action))
        {
            bool repeatable = key.Name is not ("ENTER" or "TAB" or "ESC") && IsRepeatable(action);
            return new(repeat && !repeatable ? null : action, true, repeatable);
        }
        // Modal contexts own all keys. No event can fall through to the workspace behind them.
        return new(null, leader || scope is KeyboardScope.Menu or KeyboardScope.Details or KeyboardScope.ElementChat or KeyboardScope.Bindings or KeyboardScope.Navbar);
    }

    private static bool IsRepeatable(KeyboardAction action) => action is
        KeyboardAction.MoveUp or KeyboardAction.MoveDown or KeyboardAction.ContentUp or KeyboardAction.ContentDown
        or KeyboardAction.ContentLeft or KeyboardAction.ContentRight or KeyboardAction.NextTab or KeyboardAction.PreviousTab
        or KeyboardAction.ResizeSmaller or KeyboardAction.ResizeLarger or KeyboardAction.GraphZoomIn or KeyboardAction.GraphZoomOut
        or KeyboardAction.TextBackspace or KeyboardAction.TextDelete or KeyboardAction.TextLeft or KeyboardAction.TextRight
        or KeyboardAction.TextUp or KeyboardAction.TextDown or KeyboardAction.TextHome or KeyboardAction.TextEnd
        or KeyboardAction.TextSelectLeft or KeyboardAction.TextSelectRight or KeyboardAction.TextSelectUp or KeyboardAction.TextSelectDown
        or KeyboardAction.TextSelectHome or KeyboardAction.TextSelectEnd or KeyboardAction.DialogNext or KeyboardAction.DialogPrevious or KeyboardAction.NextNavbar or KeyboardAction.PreviousNavbar;

    private static bool Applies(KeyboardAction action, KeyboardScope scope, bool resize)
    {
        bool normal = scope is KeyboardScope.Explorer or KeyboardScope.Tabs or KeyboardScope.TextEditor
            or KeyboardScope.History or KeyboardScope.Content or KeyboardScope.Graph or KeyboardScope.Navbar;
        return action switch
        {
            KeyboardAction.Quit => true,
            KeyboardAction.ToggleNavbarFocus => normal,
            KeyboardAction.NextNavbar or KeyboardAction.PreviousNavbar or KeyboardAction.ConfirmNavbar or KeyboardAction.LeaveNavbar => scope == KeyboardScope.Navbar,
            KeyboardAction.CancelResize => normal && resize,
            KeyboardAction.OpenMenu or KeyboardAction.EditKeybindings or KeyboardAction.ToggleDock or KeyboardAction.DockLeft
                or KeyboardAction.DockRight or KeyboardAction.ResizeSmaller or KeyboardAction.ResizeLarger or KeyboardAction.ResetSizes
                or KeyboardAction.NextPanel or KeyboardAction.PreviousPanel or KeyboardAction.FocusFiles or KeyboardAction.FocusChat
                or KeyboardAction.FocusCode or KeyboardAction.FocusVisualizer or KeyboardAction.ToggleTabFocus => normal,
            KeyboardAction.NextTab or KeyboardAction.PreviousTab or KeyboardAction.ConfirmTab or KeyboardAction.ReturnToContent => scope == KeyboardScope.Tabs,
            KeyboardAction.MoveUp or KeyboardAction.MoveDown or KeyboardAction.OpenSelection or KeyboardAction.ExpandSelection or KeyboardAction.Collapse => scope == KeyboardScope.Explorer,
            KeyboardAction.ContentUp or KeyboardAction.ContentDown or KeyboardAction.ContentLeft or KeyboardAction.ContentRight => scope is KeyboardScope.Content or KeyboardScope.Graph or KeyboardScope.History,
            KeyboardAction.GraphZoomIn or KeyboardAction.GraphZoomOut or KeyboardAction.GraphResetZoom or KeyboardAction.GraphEnterComponent
                or KeyboardAction.GraphExitComponent or KeyboardAction.GraphOptions => scope == KeyboardScope.Graph,
            KeyboardAction.ToggleHistory => scope is KeyboardScope.TextEditor or KeyboardScope.History,
            KeyboardAction.TextSubmit or KeyboardAction.TextNewline => scope is KeyboardScope.TextEditor or KeyboardScope.ElementChat,
            >= KeyboardAction.TextBackspace and <= KeyboardAction.TextPaste => scope is KeyboardScope.TextEditor or KeyboardScope.ElementChat or KeyboardScope.Bindings,
            KeyboardAction.DialogClose => !normal,
            KeyboardAction.DialogNext or KeyboardAction.DialogPrevious or KeyboardAction.DialogConfirm => scope == KeyboardScope.Menu,
            KeyboardAction.BindingNext or KeyboardAction.BindingPrevious or KeyboardAction.BindingApply or KeyboardAction.BindingSave => scope == KeyboardScope.Bindings,
            _ => false
        };
    }
}
