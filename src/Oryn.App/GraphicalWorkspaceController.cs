using System.Collections.Concurrent;
using System.Diagnostics;

namespace Oryn.App;

public sealed record WorkspaceMenuItem(string Label, KeyboardAction? Action);

public enum WorkspaceOverlay { None, Menu, Options, Details, ElementChat, Keybindings }
public enum ClipboardRequest { None, Copy, Cut, Paste }
public readonly record struct KeyboardInputResult(bool Handled, ClipboardRequest Clipboard = ClipboardRequest.None, bool AllowCommittedText = false)
{
    public static KeyboardInputResult Consumed => new(true);
}

/// <summary>Toolkit-independent interaction adapter for the graphical M0 workspace.</summary>
public sealed class GraphicalWorkspaceController : IDisposable
{
    public const int CellWidth = 9, CellHeight = 22;
    public WorkspaceState State { get; }
    public KeyboardRouter Keyboard { get; private set; }
    public WorkspaceLayout Layout => WorkspaceLayout.Calculate(Columns, Rows, State.Focus, State.LayoutOptions);
    public int Columns { get; private set; } = 140;
    public int Rows { get; private set; } = 38;
    public int ExplorerTop { get; private set; }
    public int CodeTop { get; private set; }
    public int CodeLeft { get; private set; }
    public int ChatScroll { get; private set; }
    public bool HistoryFocused { get; private set; }
    public WorkspaceOverlay Overlay { get; private set; }
    public int OverlaySelection { get; private set; }
    public bool NavbarFocused { get; private set; }
    public int NavbarIndex { get; private set; }
    public int? OpenNavbarIndex { get; private set; }
    public static IReadOnlyList<string> NavbarLabels { get; } = Array.AsReadOnly(new[] { "File", "Project", "View", "Agents", "Tools", "Settings" });
    private static readonly WorkspaceMenuItem[] WorkspaceMenu = [
        new("Keybindings", KeyboardAction.EditKeybindings), new("Toggle visualizer dock", KeyboardAction.ToggleDock),
        new("Dock visualizer left", KeyboardAction.DockLeft), new("Dock visualizer right", KeyboardAction.DockRight),
        new("Reset panel widths", KeyboardAction.ResetSizes), new("Quit", KeyboardAction.Quit)];
    public IReadOnlyList<WorkspaceMenuItem> MenuItems => OpenNavbarIndex switch
    {
        0 => [WorkspaceMenu[5]],
        2 => WorkspaceMenu[1..5],
        5 => [WorkspaceMenu[0]],
        null => WorkspaceMenu,
        _ => [new("No actions available yet", null)]
    };
    public string MenuTitle => OpenNavbarIndex is { } index ? NavbarLabels[index] : "Workspace";
    public PreviewGraphTarget? OverlayTarget { get; private set; }
    public TextEditState Editor { get; } = new();
    public TextEditState BindingEditor { get; } = new();
    public KeyBindingSettings? BindingDraft { get; private set; }
    public int BindingIndex { get; private set; } = -1;
    public string BindingError { get; private set; } = "";
    public string Composition { get; set; } = "";
    public bool Dirty { get; set; } = true;
    public bool Loading { get; private set; }
    public bool AcceptsText => Overlay == WorkspaceOverlay.Keybindings || Overlay == WorkspaceOverlay.ElementChat
        || Overlay == WorkspaceOverlay.None && !NavbarFocused && State.Focus == WorkspacePanel.Center && State.ActiveTab == WorkspaceTab.Chat
            && State.FocusArea == PanelFocusArea.Content && !HistoryFocused;
    public ChatPreviewState CurrentChat => Overlay == WorkspaceOverlay.ElementChat && OverlayTarget is { } target
        ? State.Graph.ChatFor(target.Id) : State.Chat;
    private readonly IWorkspaceFiles _files;
    private readonly string _profilePath;
    private readonly ConcurrentQueue<Action> _completed = new();
    private CancellationTokenSource? _load;
    private readonly Dictionary<ChatPreviewState, long> _thinking = [];
    private Task? _loadingTask;
    private long _lastMotion;
    private System.Numerics.Vector2 _graphOrigin;
    public GraphicalWorkspaceController(WorkspaceState state, IWorkspaceFiles files, KeyboardRouter keyboard, string profilePath)
    { State = state; _files = files; Keyboard = keyboard; _profilePath = profilePath; Editor.Set(state.Chat.Draft); }
    public void Resize(int width, int height)
    {
        int columns = Math.Max(1, width / CellWidth), rows = Math.Max(1, height / CellHeight);
        if (columns == Columns && rows == Rows) return;
        Columns = columns; Rows = rows; KeepExplorerVisible(); Dirty = true;
    }
    public void Tick(long now)
    {
        if (State.Graph.Placement.Advance(_lastMotion == 0 ? 1f / 60 : (now - _lastMotion) / 1000f)) Dirty = true;
        _lastMotion = now;
        while (_completed.TryDequeue(out Action? action)) { action(); Dirty = true; }
        foreach ((ChatPreviewState chat, long started) in _thinking.ToArray())
            if (now - started >= 1600) { chat.CompleteReply(); _thinking.Remove(chat); Dirty = true; }
        if (Keyboard.Expire(now)) Dirty = true;
    }
    public bool Animating => State.Graph.Placement.Moving || (Overlay == WorkspaceOverlay.ElementChat ? CurrentChat.IsThinking
        : Overlay == WorkspaceOverlay.None && State.ActiveTab == WorkspaceTab.Chat && Layout.Center.Width > 0 && State.Chat.IsThinking);
    public void Text(string text)
    {
        if (!AcceptsText) return;
        if (Overlay == WorkspaceOverlay.Keybindings) BindingEditor.Insert(text);
        else { Editor.Insert(text); CurrentChat.Draft = Editor.Text; ChatScroll = 0; }
        Composition = ""; Dirty = true;
    }
    public KeyboardScope InputScope => Overlay switch
    {
        WorkspaceOverlay.Menu or WorkspaceOverlay.Options => KeyboardScope.Menu,
        WorkspaceOverlay.Details => KeyboardScope.Details,
        WorkspaceOverlay.ElementChat => KeyboardScope.ElementChat,
        WorkspaceOverlay.Keybindings => KeyboardScope.Bindings,
        _ => NavbarFocused ? KeyboardScope.Navbar : State.Focus == WorkspacePanel.Explorer ? KeyboardScope.Explorer
            : State.FocusArea == PanelFocusArea.Tabs ? KeyboardScope.Tabs
            : GraphActive ? KeyboardScope.Graph
            : AcceptsText ? KeyboardScope.TextEditor
            : State.Focus == WorkspacePanel.Center && State.ActiveTab == WorkspaceTab.Chat ? KeyboardScope.History : KeyboardScope.Content
    };
    public KeyboardInputResult Key(InputKey key, long now, bool repeat = false)
    {
        Dirty = true;
        if (Composition.Length > 0 && key.Modifiers is InputModifiers.None or InputModifiers.Shift
            && !(Overlay == WorkspaceOverlay.None && key == new InputKey("TAB", InputModifiers.Shift)))
        {
            if (key.Name == "ESC") { Composition = ""; return KeyboardInputResult.Consumed; }
            return new(true, AllowCommittedText: true);
        }
        if (key == new InputKey("ESC") && State.Graph.Placement.DraggedId is not null)
        { State.Graph.Placement.End(cancel: true); return KeyboardInputResult.Consumed; }
        KeyboardContext context = new(State.Focus, State.FocusArea, State.ResizingSplitter is not null,
            ContentAcceptsText: AcceptsText, GraphActive: GraphActive, Scope: InputScope);
        KeyRoutingResult result = Keyboard.Route(key, context, now, repeat);
        if (result.Action is not { } action) return new(result.Handled);
        ClipboardRequest clipboard = action switch
        { KeyboardAction.TextCopy => ClipboardRequest.Copy, KeyboardAction.TextCut => ClipboardRequest.Cut,
          KeyboardAction.TextPaste => ClipboardRequest.Paste, _ => ClipboardRequest.None };
        if (clipboard != ClipboardRequest.None) return new(true, clipboard);
        Execute(action, now);
        return KeyboardInputResult.Consumed;
    }
    public bool GraphActive => State.Focus == WorkspacePanel.Visualizer || State.Focus == WorkspacePanel.Center && State.ActiveTab == WorkspaceTab.Visualizer;
    public void Execute(KeyboardAction action, long now)
    {
        State.Graph.Placement.End();
        Dirty = true;
        if (action is not (KeyboardAction.ToggleNavbarFocus or KeyboardAction.NextNavbar or KeyboardAction.PreviousNavbar
            or KeyboardAction.ConfirmNavbar or KeyboardAction.LeaveNavbar or KeyboardAction.DialogClose
            or KeyboardAction.DialogNext or KeyboardAction.DialogPrevious or KeyboardAction.DialogConfirm)) NavbarFocused = false;
        if (action is KeyboardAction.NextPanel or KeyboardAction.PreviousPanel
            or KeyboardAction.FocusFiles or KeyboardAction.FocusChat or KeyboardAction.FocusCode or KeyboardAction.FocusVisualizer
            or KeyboardAction.ToggleTabFocus or KeyboardAction.NextTab or KeyboardAction.PreviousTab
            or KeyboardAction.ConfirmTab or KeyboardAction.ReturnToContent
            or KeyboardAction.ToggleDock or KeyboardAction.DockLeft or KeyboardAction.DockRight
            or KeyboardAction.MoveUp or KeyboardAction.MoveDown or KeyboardAction.Collapse)
        {
            CancelPendingRead(); HistoryFocused = false; Composition = "";
        }
        switch (action)
        {
            case KeyboardAction.ToggleNavbarFocus:
                NavbarFocused = !NavbarFocused; CancelPendingRead(); Keyboard.CancelLeader(); Composition = ""; State.EndResize(); break;
            case KeyboardAction.NextNavbar: NavbarIndex = (NavbarIndex + 1) % NavbarLabels.Count; break;
            case KeyboardAction.PreviousNavbar: NavbarIndex = (NavbarIndex + NavbarLabels.Count - 1) % NavbarLabels.Count; break;
            case KeyboardAction.ConfirmNavbar: OpenNavbarMenu(NavbarIndex); break;
            case KeyboardAction.LeaveNavbar: NavbarFocused = false; break;
            case KeyboardAction.ToggleHistory: HistoryFocused = !HistoryFocused; Composition = ""; break;
            case KeyboardAction.DialogClose: CloseOverlay(); break;
            case KeyboardAction.DialogNext: OverlaySelection = (OverlaySelection + 1) % (Overlay == WorkspaceOverlay.Options ? 3 : MenuItems.Count); break;
            case KeyboardAction.DialogPrevious: OverlaySelection = (OverlaySelection + (Overlay == WorkspaceOverlay.Options ? 2 : MenuItems.Count - 1)) % (Overlay == WorkspaceOverlay.Options ? 3 : MenuItems.Count); break;
            case KeyboardAction.DialogConfirm: ChooseOverlay(OverlaySelection, now); break;
            case KeyboardAction.BindingNext: SelectBinding(BindingIndex + 1); break;
            case KeyboardAction.BindingPrevious: SelectBinding(BindingIndex - 1); break;
            case KeyboardAction.BindingApply: ApplyBindings(false); break;
            case KeyboardAction.BindingSave: ApplyBindings(true); break;
            case >= KeyboardAction.TextSubmit and <= KeyboardAction.TextSelectAll: Edit(action, now); break;
            case KeyboardAction.Quit: State.Dispatch(WorkspaceCommand.Quit); break;
            case KeyboardAction.OpenMenu: OpenOverlay(WorkspaceOverlay.Menu); break;
            case KeyboardAction.EditKeybindings: OpenBindings(); break;
            case KeyboardAction.NextPanel: State.Dispatch(WorkspaceCommand.NextPanel); HistoryFocused = false; break;
            case KeyboardAction.PreviousPanel: State.Dispatch(WorkspaceCommand.PreviousPanel); HistoryFocused = false; break;
            case KeyboardAction.FocusFiles: State.SetFocus(WorkspacePanel.Explorer); break;
            case KeyboardAction.FocusChat: State.Dispatch(WorkspaceCommand.ChatTab); break;
            case KeyboardAction.FocusCode: State.Dispatch(WorkspaceCommand.CodeTab); break;
            case KeyboardAction.FocusVisualizer: State.ShowVisualizer(); break;
            case KeyboardAction.ToggleTabFocus: State.Dispatch(WorkspaceCommand.ToggleTabFocus); break;
            case KeyboardAction.NextTab: State.Dispatch(WorkspaceCommand.NextTab); break;
            case KeyboardAction.PreviousTab: State.Dispatch(WorkspaceCommand.PreviousTab); break;
            case KeyboardAction.ConfirmTab: State.Dispatch(WorkspaceCommand.ConfirmTab); break;
            case KeyboardAction.ReturnToContent: State.Dispatch(WorkspaceCommand.ReturnToContent); break;
            case KeyboardAction.OpenSelection: OpenSelection(); break;
            case KeyboardAction.ExpandSelection: ExpandSelection(); break;
            case KeyboardAction.Collapse: State.Dispatch(WorkspaceCommand.Collapse); KeepExplorerVisible(); break;
            case KeyboardAction.MoveUp: State.Dispatch(WorkspaceCommand.MoveUp); KeepExplorerVisible(); break;
            case KeyboardAction.MoveDown: State.Dispatch(WorkspaceCommand.MoveDown); KeepExplorerVisible(); break;
            case KeyboardAction.ToggleDock: State.ToggleVisualizerDock(); break;
            case KeyboardAction.DockLeft: State.SetVisualizerPlacement(VisualizerPlacement.Left); break;
            case KeyboardAction.DockRight: State.SetVisualizerPlacement(VisualizerPlacement.Right); break;
            case KeyboardAction.ResetSizes: State.ResetPanelSizes(); break;
            case KeyboardAction.ResizeSmaller: State.ResizeFocusedPanel(-2, Columns, Rows); break;
            case KeyboardAction.ResizeLarger: State.ResizeFocusedPanel(2, Columns, Rows); break;
            case KeyboardAction.CancelResize: State.EndResize(true); break;
            case KeyboardAction.GraphZoomIn: State.Graph.ChangeZoom(1); break;
            case KeyboardAction.GraphZoomOut: State.Graph.ChangeZoom(-1); break;
            case KeyboardAction.GraphResetZoom: State.Graph.ResetZoom(); break;
            case KeyboardAction.GraphExitComponent: State.Graph.ExitComponent(); break;
            case KeyboardAction.GraphEnterComponent:
                if (!State.Graph.EnterComponent()) OpenOverlay(WorkspaceOverlay.Details); break;
            case KeyboardAction.GraphOptions: OpenOverlay(WorkspaceOverlay.Options); break;
            case KeyboardAction.ContentUp: Navigate(0, -1); break;
            case KeyboardAction.ContentDown: Navigate(0, 1); break;
            case KeyboardAction.ContentLeft: Navigate(-1, 0); break;
            case KeyboardAction.ContentRight: Navigate(1, 0); break;
        }
    }
    private void Edit(KeyboardAction action, long now)
    {
        TextEditState editor = Overlay == WorkspaceOverlay.Keybindings ? BindingEditor : Editor;
        switch (action)
        {
            case KeyboardAction.TextSubmit:
                if (CurrentChat.Send()) { editor.Set(""); _thinking[CurrentChat] = now; ChatScroll = 0; } break;
            case KeyboardAction.TextNewline: editor.Insert("\n"); break;
            case KeyboardAction.TextBackspace: editor.Backspace(); break;
            case KeyboardAction.TextDelete: editor.Delete(); break;
            case KeyboardAction.TextSelectAll: editor.SelectAll(); break;
            default:
                string name = action.ToString()[4..];
                bool select = name.StartsWith("Select");
                editor.Move((select ? name[6..] : name).ToUpperInvariant(), select); break;
        }
        if (Overlay != WorkspaceOverlay.Keybindings) CurrentChat.Draft = editor.Text;
    }
    private void ExpandSelection()
    {
        if (State.Selected is not { Entry.IsDirectory: true, Entry.IsLink: false } row) return;
        if (State.ExpandedDirectories.Contains(row.Entry.Path))
        {
            if (State.SelectedIndex + 1 < State.Rows.Count && State.Rows[State.SelectedIndex + 1].Depth > row.Depth)
            { State.Select(State.SelectedIndex + 1); KeepExplorerVisible(); }
            return;
        }
        if (!Loading) OpenSelection();
    }
    private void Navigate(int dx, int dy)
    {
        if (GraphActive) State.Graph.Move(dx, dy);
        else if (State.ActiveTab == WorkspaceTab.Code) { CodeTop = Math.Max(0, CodeTop + dy); CodeLeft = Math.Max(0, CodeLeft + dx * 3); }
        else ChatScroll = Math.Max(0, ChatScroll - dy);
    }
    public void Wheel(int x, int y, int dx, int dy)
    {
        Dirty = true;
        if (Overlay == WorkspaceOverlay.ElementChat) { ChatScroll = Math.Max(0, ChatScroll + dy * 3); return; }
        if (Overlay != WorkspaceOverlay.None) return;
        WorkspacePanel? panel = Layout.HitTest(x, y);
        if (panel == WorkspacePanel.Explorer) ExplorerTop = Math.Clamp(ExplorerTop - dy * 3, 0, Math.Max(0, State.Rows.Count - Math.Max(1, Layout.Explorer.Height - 3)));
        else if (panel is not null)
        {
            bool graphHovered = panel == WorkspacePanel.Visualizer || panel == WorkspacePanel.Center && State.ActiveTab == WorkspaceTab.Visualizer;
            if (graphHovered) State.Graph.ChangeZoom(Math.Sign(dy));
            else if (State.ActiveTab == WorkspaceTab.Code) { CodeTop = Math.Max(0, CodeTop - dy * 3); CodeLeft = Math.Max(0, CodeLeft + dx * 3); }
            else ChatScroll = Math.Max(0, ChatScroll + dy * 3);
        }
    }
    public void PointerMove(int x) { if (State.ResizingSplitter is not null) { State.UpdateResize(x, Columns, Rows); Dirty = true; } }
    public void BeginGraphDrag(Guid id, System.Numerics.Vector2 pointer, System.Numerics.Vector2 origin)
    { _graphOrigin = origin; State.Graph.Placement.Begin(id, pointer - origin); Dirty = true; }
    public void PointerMove(float x, float y)
    {
        PointerMove((int)(x / CellWidth));
        if (State.Graph.Placement.DraggedId is not null) { State.Graph.Placement.Target(new System.Numerics.Vector2(x, y) - _graphOrigin); Dirty = true; }
    }
    public void PointerUp() { State.EndResize(); State.Graph.Placement.End(); Dirty = true; }
    public void FocusLost() { State.Graph.Placement.End(); State.EndResize(true); Keyboard.CancelLeader(); Composition = ""; Dirty = true; }
    public void FocusPanel(WorkspacePanel panel)
    { NavbarFocused = false; State.Graph.Placement.End(); CancelPendingRead(); Keyboard.CancelLeader(); Composition = ""; State.SetFocus(panel); HistoryFocused = false; Dirty = true; }
    public void SelectExplorer(int index) { FocusPanel(WorkspacePanel.Explorer); State.Select(index); KeepExplorerVisible(); OpenSelection(); }
    public void SelectTab(WorkspaceTab tab)
    {
        NavbarFocused = false;
        CancelPendingRead(); Keyboard.CancelLeader(); Composition = ""; HistoryFocused = false;
        State.Dispatch(tab switch { WorkspaceTab.Chat => WorkspaceCommand.ChatTab, WorkspaceTab.Code => WorkspaceCommand.CodeTab, _ => WorkspaceCommand.VisualizerTab });
        State.SetFocus(WorkspacePanel.Center, PanelFocusArea.Tabs); Dirty = true;
    }
    public void FocusHistory(bool history)
    { if (Overlay == WorkspaceOverlay.None) FocusPanel(WorkspacePanel.Center); HistoryFocused = history; Dirty = true; }
    public void PlaceVisualizer(VisualizerPlacement placement)
    {
        NavbarFocused = false;
        CancelPendingRead(); Keyboard.CancelLeader(); HistoryFocused = false; Composition = "";
        State.SetVisualizerPlacement(placement); Dirty = true;
    }
    public void OpenSelection()
    {
        CancelPendingRead();
        if (State.ToggleExpandedSelection()) { Dirty = true; return; }
        _load = new(); CancellationToken token = _load.Token;
        Loading = true; Dirty = true;
        _loadingTask = LoadAsync(token);
    }
    private void CancelPendingRead()
    { _load?.Cancel(); _load?.Dispose(); _load = null; Loading = false; }
    private async Task LoadAsync(CancellationToken token)
    {
        try
        {
            SelectionResult? result = await State.LoadSelectionAsync(_files, token);
            _completed.Enqueue(() => { if (!token.IsCancellationRequested) { if (result is not null) State.Apply(result); CodeTop = CodeLeft = 0; HistoryFocused = false; Loading = false; } });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _completed.Enqueue(() => { if (!token.IsCancellationRequested) { State.SetStatus(ex.Message); Loading = false; } }); }
    }
    private void KeepExplorerVisible()
    {
        int visible = Math.Max(1, Layout.Explorer.Height - 3);
        if (State.SelectedIndex < ExplorerTop) ExplorerTop = State.SelectedIndex;
        if (State.SelectedIndex >= ExplorerTop + visible) ExplorerTop = State.SelectedIndex - visible + 1;
        ExplorerTop = Math.Clamp(ExplorerTop, 0, Math.Max(0, State.Rows.Count - visible));
    }
    public void OpenNavbarMenu(int index)
    {
        OpenOverlay(WorkspaceOverlay.Menu);
        NavbarIndex = Math.Clamp(index, 0, NavbarLabels.Count - 1); OpenNavbarIndex = NavbarIndex;
    }
    public void OpenOverlay(WorkspaceOverlay overlay)
    {
        State.Graph.Placement.End(); CancelPendingRead(); Keyboard.CancelLeader();
        NavbarFocused = false; OpenNavbarIndex = null;
        Overlay = overlay; OverlaySelection = 0; Composition = "";
        if (overlay is WorkspaceOverlay.Options or WorkspaceOverlay.Details or WorkspaceOverlay.ElementChat) OverlayTarget = State.Graph.Target;
        if (overlay == WorkspaceOverlay.ElementChat) { Editor.Set(CurrentChat.Draft); ChatScroll = 0; }
        Dirty = true;
    }
    public void CloseOverlay()
    {
        if (Overlay == WorkspaceOverlay.ElementChat) Editor.Set(State.Chat.Draft);
        NavbarFocused = OpenNavbarIndex is not null; OpenNavbarIndex = null;
        Overlay = WorkspaceOverlay.None; OverlayTarget = null; Composition = ""; Dirty = true;
    }
    public void ChooseOverlay(int index, long now)
    {
        if (Overlay == WorkspaceOverlay.Options)
        {
            if (index == 0) OpenOverlay(WorkspaceOverlay.Details);
            else if (index == 1) OpenOverlay(WorkspaceOverlay.ElementChat);
            else CloseOverlay();
        }
        else if (Overlay == WorkspaceOverlay.Menu && index >= 0 && index < MenuItems.Count && MenuItems[index].Action is { } action)
        {
            CloseOverlay(); Execute(action, now);
        }
    }

    public void OpenBindings() { BindingDraft = Keyboard.Settings.DeepCopy(); BindingIndex = -1; BindingEditor.Set(BindingDraft.Super); BindingError = ""; OpenOverlay(WorkspaceOverlay.Keybindings); }
    private bool CommitBinding()
    {
        if (BindingDraft is null) return false;
        if (BindingIndex < 0) BindingDraft = BindingDraft with { Super = BindingEditor.Text.Trim() };
        else
        {
            KeyBindingDefinition definition = KeyBindingSettings.Definitions[BindingIndex];
            BindingDraft.Group(definition.Group)[definition.Action.ToString()] = BindingEditor.Text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        }
        return true;
    }
    public void SelectBinding(int index)
    {
        if (!CommitBinding()) return;
        BindingIndex = Math.Clamp(index, -1, KeyBindingSettings.Definitions.Count - 1);
        BindingEditor.Set(BindingIndex < 0 ? BindingDraft!.Super : string.Join(", ", BindingDraft!.Bindings(KeyBindingSettings.Definitions[BindingIndex])));
        Dirty = true;
    }
    public void ResetBindings() { BindingDraft = new(); BindingIndex = -1; BindingEditor.Set(BindingDraft.Super); BindingError = ""; Dirty = true; }
    public bool ApplyBindings(bool save)
    {
        if (!CommitBinding()) return false;
        try
        {
            KeyboardRouter router = new(BindingDraft!);
            if (save) KeyBindingStore.Save(_profilePath, BindingDraft!);
            Keyboard = router; CloseOverlay(); State.SetStatus(save ? "Keybindings saved" : "Keybindings applied for this session"); return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        { BindingError = ex.Message; Dirty = true; return false; }
    }
    public void Dispose()
    {
        _load?.Cancel();
        // Observe completion without blocking native shutdown or accepting stale results.
        if (_loadingTask is { } task) _ = task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
        _load?.Dispose();
    }
    public static long Now => (long)(Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
}
