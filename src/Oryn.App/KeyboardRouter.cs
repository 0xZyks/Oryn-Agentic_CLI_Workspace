namespace Oryn.App;

public sealed record KeyboardContext(WorkspacePanel Panel, PanelFocusArea Area, bool Resizing = false,
    bool ContentAcceptsTab = false, bool ContentAcceptsText = false, bool GraphActive = false);
public readonly record struct KeyRoutingResult(KeyboardAction? Action, bool Handled);

/// <summary>Framework-independent grouped shortcuts with contextual scopes and an optional leader.</summary>
public sealed class KeyboardRouter
{
    private sealed record Binding(KeyBindingDefinition Definition, InputKey Key, bool Leader);
    private readonly List<Binding> _bindings = [];
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
                Binding binding = new(definition, key, usesSuper && _leader is not null);
                if (_leader is { } leader && key == leader && !binding.Leader)
                    throw new ArgumentException($"{chord} is reserved as the Super leader.");
                foreach (Binding other in _bindings)
                    if (key == other.Key && binding.Leader == other.Leader && Overlap(definition, other.Definition)
                        && !(key == InputKey.Parse("Alt+Enter") &&
                            (definition.Action == KeyboardAction.GraphOptions && other.Definition.Action == KeyboardAction.ToggleTabFocus
                                || other.Definition.Action == KeyboardAction.GraphOptions && definition.Action == KeyboardAction.ToggleTabFocus)))
                        throw new ArgumentException($"Shortcut conflict: {chord} ({definition.Action} / {other.Definition.Action}).");
                _bindings.Add(binding);
            }
        }
    }

    public bool Expire(long now)
    {
        if (_pendingUntil is null || now < _pendingUntil) return false;
        _pendingUntil = null;
        return true;
    }
    public void CancelLeader() => _pendingUntil = null;
    public string Hint(KeyboardAction action)
    {
        KeyBindingDefinition definition = KeyBindingSettings.Definitions.First(definition => definition.Action == action);
        return string.Join(" / ", Settings.Bindings(definition));
    }

    public KeyRoutingResult Route(InputKey key, KeyboardContext context, long now)
    {
        Expire(now);
        bool textLeader = _leader is { Modifiers: InputModifiers.None } prefix && (prefix.Name == "SPACE" || prefix.Name.Length == 1);
        if (context.ContentAcceptsText && textLeader) CancelLeader();
        if (_leader == key && !(context.ContentAcceptsText && textLeader))
        { _pendingUntil = now + _timeout; return new(null, true); }
        bool leader = LeaderPending;
        _pendingUntil = null;
        if (leader && key == new InputKey("ESC")) return new(null, true);
        foreach (Binding binding in _bindings.OrderByDescending(binding => binding.Definition.Action == KeyboardAction.CancelResize)
            .ThenByDescending(binding => binding.Definition.Action == KeyboardAction.GraphOptions))
        {
            if (binding.Leader != leader || binding.Key != key || !Applies(binding.Definition, context)) continue;
            if (context.ContentAcceptsTab && context.Area == PanelFocusArea.Content && key.Name == "TAB"
                && key.Modifiers is InputModifiers.None or InputModifiers.Shift && !binding.Leader) continue;
            return new(binding.Definition.Action, true);
        }
        return new(null, leader); // Consume an unknown leader sequence, not a text/content command.
    }

    private static bool Applies(KeyBindingDefinition definition, KeyboardContext context) => definition.Group switch
    {
        KeyBindingGroup.Workspace => definition.Action != KeyboardAction.CancelResize || context.Resizing,
        KeyBindingGroup.Panels => true,
        KeyBindingGroup.Tabs => context.Panel == WorkspacePanel.Center
            && (definition.Action == KeyboardAction.ToggleTabFocus || context.Area == PanelFocusArea.Tabs),
        KeyBindingGroup.Explorer => context.Panel == WorkspacePanel.Explorer,
        KeyBindingGroup.Graph => context.GraphActive && context.Area == PanelFocusArea.Content,
        _ => context.Panel != WorkspacePanel.Explorer && context.Area == PanelFocusArea.Content
    };

    private static bool Overlap(KeyBindingDefinition a, KeyBindingDefinition b)
    {
        if (a.Action == KeyboardAction.CancelResize && b.Group != KeyBindingGroup.Workspace
            || b.Action == KeyboardAction.CancelResize && a.Group != KeyBindingGroup.Workspace) return false;
        if (a.Group == KeyBindingGroup.Workspace || b.Group == KeyBindingGroup.Workspace
            || a.Group == KeyBindingGroup.Panels || b.Group == KeyBindingGroup.Panels) return true;
        if (a.Group == b.Group) return true;
        if (a.Group is KeyBindingGroup.Graph && b.Group is KeyBindingGroup.Content
            || b.Group is KeyBindingGroup.Graph && a.Group is KeyBindingGroup.Content) return true;
        return (a.Action == KeyboardAction.ToggleTabFocus && b.Group is KeyBindingGroup.Content or KeyBindingGroup.Graph)
            || (b.Action == KeyboardAction.ToggleTabFocus && a.Group is KeyBindingGroup.Content or KeyBindingGroup.Graph);
    }
}
