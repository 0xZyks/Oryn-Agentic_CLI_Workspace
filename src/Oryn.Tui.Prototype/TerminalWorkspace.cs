using System.Collections.ObjectModel;
using Oryn.App;
using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Oryn.Tui.Prototype;

/// <summary>Temporary adapter. Application state, commands and layout remain in Oryn.App.</summary>
public sealed class TerminalWorkspace(WorkspaceState state, IWorkspaceFiles files, TerminalCapabilities capabilities,
    RenderCadence cadence, IDisplayRefreshRateProbe refreshRateProbe, KeyboardRouter keyboard, string keybindingsPath)
{
    private readonly WorkspaceTheme _theme = new(capabilities.Color);
    private readonly PanelFocusAccent _focusAccent = new(new WorkspaceTheme(capabilities.Color), capabilities.Unicode);
    private readonly ListView _explorer = new() { X = 2, Y = 4, Width = Dim.Fill(2), Height = Dim.Fill(1) };
    private readonly View _explorerPanel = new() { CanFocus = true };
    private readonly View _centerPanel = new() { CanFocus = true };
    private readonly View _codeCanvas = new() { X = 2, Y = 5, Width = Dim.Fill(2), Height = Dim.Fill(1), CanFocus = true };
    private readonly View _visualizerPanel = new() { CanFocus = true };
    private readonly View _appBar = new() { Width = Dim.Fill(), Height = 3 };
    private readonly Label _brand = new() { X = 2, Y = 1, Text = "oryn", Width = 6, Height = 1 };
    private readonly Label _projectName = new() { X = 10, Y = 1, Width = Dim.Fill(28), Height = 1 };
    private readonly Label _filesHeading = new() { X = 2, Y = 1, Width = Dim.Fill(2), Height = 1 };
    private readonly Label _filesCaption = new() { X = 2, Y = 2, Width = Dim.Fill(2), Height = 1 };
    private readonly Label _mapHeading = new() { X = 2, Y = 1, Width = Dim.Fill(2), Height = 1 };
    private readonly Label _mapCaption = new() { X = 2, Y = 2, Text = "Architecture", Width = Dim.Fill(2), Height = 1 };
    private readonly Label _chatTab = FlatTab("Chat", 2);
    private readonly Label _codeTab = FlatTab("Code", 11);
    private readonly Label _visualizerTab = FlatTab("Visualizer", 20);
    private readonly Label _tabFocusAccent = new() { CanFocus = false, Visible = false, Height = 1 };
    private readonly Label _dockAction = FlatTab("Open as tab", 2);
    private readonly Label _dockLeft = FlatTab("Left", 2);
    private readonly Label _dockRight = FlatTab("Right", 10);
    private readonly Label _explorerGrip = new() { TextAlignment = Alignment.Center, VerticalTextAlignment = Alignment.Center };
    private readonly Label _visualizerGrip = new() { TextAlignment = Alignment.Center, VerticalTextAlignment = Alignment.Center };
    private readonly Label _context = new() { X = 2, Y = 3, Width = Dim.Fill(2), Height = 1 };
    private readonly View _chatComposer = new() { X = 3, Y = Pos.AnchorEnd(4), Width = Dim.Fill(3), Height = 3, BorderStyle = LineStyle.None, CanFocus = true };
    private readonly Label _chatPromptMarker = new() { X = 1, Text = ">", Width = 1, Height = 1, CanFocus = false };
#pragma warning disable CS0618
    private readonly TextView _chatInput = new() { X = 3, Width = Dim.Fill(1), Height = 1, BorderStyle = LineStyle.None, Multiline = true, WordWrap = true, TabKeyAddsTab = false };
#pragma warning restore CS0618
    private (int Width, int Count, bool Thinking, int Frame)? _chatRendering;
    private long _chatThinkingStarted;
    private int _chatThinkingFrame;
    private readonly Label _mapTitle = new() { X = 2, Y = 5, Text = "Project map", Width = Dim.Fill(2), Height = 1 };
    // Read-only text stays inside the provisional adapter until editing is implemented.
#pragma warning disable CS0618
    private readonly TextView _chat = new() { X = 3, Y = 5, Width = Dim.Fill(3), Height = Dim.Fill(2), BorderStyle = LineStyle.None, ReadOnly = true, WordWrap = true, TabKeyAddsTab = false };
    private readonly TextView _code = new() { X = 2, Y = 5, Width = Dim.Fill(2), Height = Dim.Fill(1), BorderStyle = LineStyle.None, ReadOnly = true, WordWrap = false, TabKeyAddsTab = false };
#pragma warning restore CS0618
    private readonly VisualizerPreviewView _visualizer = new(state.Graph, capabilities) { X = 2, Y = 6, Width = Dim.Fill(2), Height = Dim.Fill(1), CanFocus = true };
    private int _graphWidth, _graphHeight;
    private readonly Label _status = new() { X = 2, Height = 1, Width = Dim.Fill(2) };
    private readonly Label _keyHints = new() { X = 2, Height = 1, Width = Dim.Fill(2) };
    private MenuBar _menu = null!;
    private IApplication _app = null!;
    private CancellationTokenSource? _loadCancellation;
    private Task? _loadTask;
    private int _loadGeneration;
    private bool _refreshing;
    private volatile bool _stopping;
    private FilePreview? _renderedPreview;
    private WorkspaceLayout? _lastLayout;
    private ExplorerRow[] _renderedRows = [];
    private int _renderedExpandedCount = -1;
    private WorkspaceTab? _lastActiveTab;
    private bool _suppressResizeClick;
    private (WorkspacePanel Focus, VisualizerPlacement Placement)? _panelAppearance;
    private readonly CancellationTokenSource _refreshRateCancellation = new();
    private Task? _refreshRateTask;
    private volatile bool _autoRefreshRate = cadence.ManualFramesPerSecond is null;
    private KeyboardRouter _keyboard = keyboard;
    private Window _workspaceWindow = null!;

    public void Run()
    {
        // Restore after both the window and the library's application have been disposed.
        using TerminalModeGuard originalTerminal = TerminalModeGuard.Capture();
        using KittyShortcutScope shortcutScope = new();
        // Detection has completed in the CLI before creating the terminal UI.
        Application.MaximumIterationsPerSecond = (ushort)cadence.FramesPerSecond;
        using IApplication app = Application.Create();
        _app = app;
        using Window window = new() { BorderStyle = LineStyle.None, Width = Dim.Fill(), Height = Dim.Fill() };
        _workspaceWindow = window;
        ConfigureViews(window);
        app.ScreenChanged += (_, _) => { state.EndResize(); Refresh(); };
        window.Initialized += (_, _) => { _panelAppearance = null; Refresh(); };
        try
        {
            app.Init();
            // Reassert after library configuration, before the first frame.
            Application.MaximumIterationsPerSecond = (ushort)cadence.FramesPerSecond;
            app.Keyboard.KeyDown += HandleKey;
            app.Mouse.MouseEvent += HandleMouse;
            Refresh();
            app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
            {
                long now = Environment.TickCount64;
                bool refresh = _keyboard.Expire(now);
                if (state.Chat.IsThinking)
                {
                    long elapsed = now - _chatThinkingStarted;
                    if (elapsed >= 1600)
                    {
                        state.Chat.CompleteReply();
                        state.SetStatus("Demo reply received");
                        refresh = true;
                    }
                    else
                    {
                        int frame = (int)(elapsed / (capabilities.Unicode ? 100 : 200)) % (capabilities.Unicode ? 10 : 4);
                        if (_chatThinkingFrame != frame)
                        {
                            _chatThinkingFrame = frame;
                            refresh |= state.ActiveTab == WorkspaceTab.Chat && _chat.Visible;
                        }
                    }
                }
                if (refresh) RefreshStatus();
                return !_stopping;
            });
            _refreshRateTask = MonitorRefreshRateAsync(_refreshRateCancellation.Token);
            app.Run(window);
        }
        finally
        {
            _stopping = true;
            _refreshRateCancellation.Cancel();
            _refreshRateTask?.GetAwaiter().GetResult();
            _refreshRateCancellation.Dispose();
            _loadCancellation?.Cancel();
            // Background work never waits for the UI thread to complete.
            _loadTask?.GetAwaiter().GetResult();
            _loadCancellation?.Dispose();
            app.Keyboard.KeyDown -= HandleKey;
            app.Mouse.MouseEvent -= HandleMouse;
        }
        // IApplication.Dispose restores terminal modes and the alternate screen.
    }

    private void ConfigureViews(Window window)
    {
        _projectName.Text = SingleLine(new DirectoryInfo(state.ProjectRoot).Name);
        _chat.WordWrap = false;
        _chatComposer.Add(_chatPromptMarker, _chatInput);
        _chatInput.Text = state.Chat.Draft;
        _chatInput.KeyBindings.ReplaceCommands(Key.A.WithCtrl, Command.SelectAll);
        _chatInput.TextChanged += (_, _) => { state.Chat.Draft = _chatInput.Text; PositionChatInput(); };
        _chatInput.ContentsChanged += (_, _) => { state.Chat.Draft = _chatInput.Text; PositionChatInput(); };
        // Local Tab moves between chat controls; Super+Tab still changes panels.
        _chatInput.KeyDown += (_, key) =>
        {
            if (key == Key.Enter) { key.Handled = true; SendChat(); }
            else if (key == Key.Enter.WithShift) { key.Handled = true; _chatInput.InvokeCommand(Command.NewLine); }
            else if (key == Key.Tab || key == Key.Tab.WithShift) { key.Handled = true; _chat.SetFocus(); }
        };
        _chat.KeyDown += (_, key) =>
        {
            if (key == Key.Tab) { key.Handled = true; _chatInput.SetFocus(); }
            else if (key == Key.Tab.WithShift) { key.Handled = true; _chatInput.SetFocus(); }
        };
        _code.Text = "Open a file\n\nSelect a file in the explorer\nand press Enter to read it here.";
        _codeCanvas.BorderStyle = capabilities.Color ? LineStyle.None : LineStyle.Single;
        _code.X = 2;
        _code.Y = 1;
        _code.Height = Dim.Fill(1);
        _codeCanvas.Add(_code);
        _visualizer.SelectionChanged += () => { SetGraphStatus(); RefreshStatus(); };
        _explorerPanel.Add(_filesHeading, _filesCaption, _explorer);
        _chatTab.Width = _codeTab.Width = 8;
        _visualizerTab.Width = 14;
        _dockAction.Y = 3;
        _dockAction.Width = 14;
        _dockLeft.Y = _dockRight.Y = 4;
        _dockLeft.Width = _dockRight.Width = 6;
        _visualizerPanel.Add(_mapHeading, _mapCaption, _dockAction, _dockLeft, _dockRight, _mapTitle, _visualizer);
        _centerPanel.Add(_chatTab, _codeTab, _visualizerTab, _context, _chat, _codeCanvas, _chatComposer, _tabFocusAccent);

        _menu = new MenuBar([
            new MenuBarItem("_File", [
                new MenuItem("Project root", "Show opened directory", () => ShowProjectRoot(), null),
                new MenuItem("_Quit", "Close Oryn", () => Execute(WorkspaceCommand.Quit), null)
            ]),
            new MenuBarItem("_View", [
                new MenuItem("_Files", "File explorer", () => Focus(WorkspacePanel.Explorer), null),
                new MenuItem("_Chat", "Project chat", () => Execute(WorkspaceCommand.ChatTab), null),
                new MenuItem("_Code", "Read-only preview", () => Execute(WorkspaceCommand.CodeTab), null),
                new MenuItem("_Visualizer", "Project map", () => Execute(WorkspaceCommand.ShowVisualizer), null),
                new MenuItem("Visualizer _tab", "Open in the center", () => Execute(WorkspaceCommand.VisualizerTab), null),
                new MenuItem("Dock _left", "Beside the explorer", () => Execute(WorkspaceCommand.DockVisualizerLeft), null),
                new MenuItem("Dock _right", "Right of the center", () => Execute(WorkspaceCommand.DockVisualizerRight), null),
                new MenuItem("_Toggle dock", "Tab or last dock side", () => Execute(WorkspaceCommand.ToggleVisualizerDock), null),
                new MenuItem("Wider panel", "Grow focused panel", () => ResizeFocusedPanel(2), null),
                new MenuItem("Narrower panel", "Shrink focused panel", () => ResizeFocusedPanel(-2), null),
                new MenuItem("Refresh: _Auto", "Follow terminal monitor (60 Hz fallback)", () => SetRenderCadence(null), null),
                new MenuItem("Refresh: _60 Hz", "Manual cadence for this session", () => SetRenderCadence(60), null),
                new MenuItem("Refresh: 1_20 Hz", "Manual cadence for this session", () => SetRenderCadence(120), null),
                new MenuItem("Refresh: 1_44 Hz", "Manual cadence for this session", () => SetRenderCadence(144), null),
                new MenuItem("Refresh details", "Monitor and detection status", () => MessageBox.Query(_app,
                    "Refresh rate", cadence.Description + "\n" + (cadence.Reading.Display is { } display
                        ? $"{display.Hertz:0.###} Hz / {display.Source} / window {display.WindowId}"
                        : cadence.Reading.UnavailableReason ?? "Unknown monitor"), "OK"), null),
                new MenuItem("Reset panel sizes", "Keep current dock side", () => Execute(WorkspaceCommand.ResetPanelSizes), null)
            ]),
            new MenuBarItem("_Help", [
                new MenuItem("_Keybindings", "Customize grouped shortcuts", EditKeybindings, null),
                new MenuItem("_Keys", "Current keyboard navigation", () => MessageBox.Query(_app, "Oryn",
                    string.Join('\n', KeyBindingSettings.Definitions.Select(definition => definition.Group + " / " + definition.Action + ": " + _keyboard.Hint(definition.Action))), "OK"), null)
            ])
        ]) { X = Pos.AnchorEnd(25), Y = 1, Width = 25, Height = 1 };
        _menu.Key = Key.Empty; // Oryn's router owns the configurable activation binding.
        _appBar.Add(_brand, _projectName, _menu);
        window.Add(_appBar, _explorerPanel, _centerPanel, _visualizerPanel, _explorerGrip, _visualizerGrip, _status, _keyHints);
        _focusAccent.AddTo(window);

        _explorer.ValueChanged += (_, _) =>
        {
            if (_refreshing || _stopping) return;
            state.Select(_explorer.SelectedItem ?? 0);
        };
        _explorer.Accepting += (_, args) => { args.Handled = true; ActivateSelection(); };
        _chatTab.Accepting += (_, args) => { args.Handled = true; SelectTab(WorkspaceCommand.ChatTab); };
        _codeTab.Accepting += (_, args) => { args.Handled = true; SelectTab(WorkspaceCommand.CodeTab); };
        _visualizerTab.Accepting += (_, args) => { args.Handled = true; SelectTab(WorkspaceCommand.VisualizerTab); };
        _dockAction.Accepting += (_, args) => { args.Handled = true; Execute(WorkspaceCommand.ToggleVisualizerDock); };
        _dockLeft.Accepting += (_, args) => { args.Handled = true; Execute(WorkspaceCommand.DockVisualizerLeft); };
        _dockRight.Accepting += (_, args) => { args.Handled = true; Execute(WorkspaceCommand.DockVisualizerRight); };
        BindFocus(_explorer, WorkspacePanel.Explorer);
        BindFocus(_chat, WorkspacePanel.Center);
        BindFocus(_chatInput, WorkspacePanel.Center);
        BindFocus(_code, WorkspacePanel.Center);
        BindFocus(_visualizer, WorkspacePanel.Visualizer);

        BindFocus(_chatTab, WorkspacePanel.Center, PanelFocusArea.Tabs);
        BindFocus(_codeTab, WorkspacePanel.Center, PanelFocusArea.Tabs);
        BindFocus(_visualizerTab, WorkspacePanel.Center, PanelFocusArea.Tabs);
        BindFocus(_dockAction, WorkspacePanel.Visualizer);
        BindFocus(_dockLeft, WorkspacePanel.Visualizer);
        BindFocus(_dockRight, WorkspacePanel.Visualizer);
        ApplyScheme(window, _theme.Canvas);
        ApplyScheme(_appBar, _theme.AppBar);
        _brand.SetScheme(_theme.Brand);
        _projectName.SetScheme(_theme.Breadcrumb);
        _status.SetScheme(_theme.Caption);
        _keyHints.SetScheme(_theme.Caption);
        _explorerGrip.SetScheme(_theme.Caption);
        _visualizerGrip.SetScheme(_theme.Caption);
        ApplyPanelPresentation();
        if (!capabilities.Unicode) ConfigureAscii();
    }

    private static Label FlatTab(string title, int x)
    {
        Label tab = new()
        {
            Text = title, X = x, Y = 1, Width = 9, Height = 1,
            CanFocus = true, BorderStyle = LineStyle.None, TextAlignment = Alignment.Center
        };
        tab.MouseBindings.Add(MouseFlags.LeftButtonClicked, Command.Accept);
        tab.KeyBindings.ReplaceCommands(Key.Enter, Command.Accept);
        tab.KeyBindings.ReplaceCommands(Key.Space, Command.Accept);
        return tab;
    }

    private string SingleLine(string text) => TerminalText.Sanitize(text).Replace('\n', ' ').Replace('\r', ' ');

    private void BindFocus(View view, WorkspacePanel panel, PanelFocusArea area = PanelFocusArea.Content) => view.HasFocusChanged += (_, _) =>
    {
        if (_refreshing || _stopping || !view.HasFocus) return;
        state.SetFocus(panel, area);
        RefreshLayout();
        RefreshStatus();
    };

    private static void ApplyScheme(View view, Scheme scheme)
    {
        view.SetScheme(scheme);
        foreach (View child in view.SubViews) ApplyScheme(child, scheme);
    }

    private static void ConfigureAscii()
    {
        TuiConfigurationBuilder.Shared.RuntimeConfig = """
            { "Glyphs": {
              "HLine": "-", "VLine": "|",
              "ULCorner": "+", "URCorner": "+", "LLCorner": "+", "LRCorner": "+",
              "ULCornerR": "+", "URCornerR": "+", "LLCornerR": "+", "LRCornerR": "+",
              "LeftTee": "+", "RightTee": "+", "TopTee": "+", "BottomTee": "+", "Cross": "+",
              "HalfLeftLine": "-", "HalfRightLine": "-", "HalfTopLine": "|", "HalfBottomLine": "|",
              "LeftArrow": "<", "RightArrow": ">", "UpArrow": "^", "DownArrow": "v",
              "LeftDefaultIndicator": "[", "RightDefaultIndicator": "]", "HorizontalEllipsis": "."
            } }
            """;
        TuiConfigurationBuilder.Shared.ApplyToStaticFacades();
    }

    private void HandleKey(object? sender, Key key)
    {
        if (!ReferenceEquals(_app.TopRunnableView, _workspaceWindow)
            || key.EventType == KeyEventType.Release || key.IsModifierOnly) return;
        InputKey? input = ToInputKey(key);
        if (input is null) return;
        bool chatContent = state.Focus == WorkspacePanel.Center && state.ActiveTab == WorkspaceTab.Chat && state.FocusArea == PanelFocusArea.Content;
        bool editing = chatContent && _chatInput.HasFocus;
        bool graphActive = state.Focus == WorkspacePanel.Visualizer || state.Focus == WorkspacePanel.Center && state.ActiveTab == WorkspaceTab.Visualizer;
        KeyRoutingResult result = _keyboard.Route(input.Value,
            new(state.Focus, state.FocusArea, state.ResizingSplitter is not null,
                chatContent, editing, graphActive), Environment.TickCount64);
        if (_menu.Active)
        {
            bool workspaceAction = result.Action is KeyboardAction.Quit or KeyboardAction.OpenMenu or KeyboardAction.EditKeybindings
                or KeyboardAction.ResizeSmaller or KeyboardAction.ResizeLarger or KeyboardAction.ToggleDock or KeyboardAction.DockLeft
                or KeyboardAction.DockRight or KeyboardAction.ResetSizes or KeyboardAction.FocusFiles or KeyboardAction.FocusChat
                or KeyboardAction.FocusCode or KeyboardAction.FocusVisualizer or KeyboardAction.ToggleTabFocus
                || result.Action is KeyboardAction.NextPanel or KeyboardAction.PreviousPanel && input.Value.Modifiers.HasFlag(InputModifiers.Ctrl)
                || result.Action is KeyboardAction.NextPanel or KeyboardAction.PreviousPanel && input.Value.Modifiers.HasFlag(InputModifiers.Alt);
            if (!workspaceAction) { _keyboard.CancelLeader(); return; }
            if (result.Action != KeyboardAction.OpenMenu) _menu.HideActiveItem();
        }
        if (!result.Handled)
        {
            if (input.Value.Name == "TAB" && input.Value.Modifiers is InputModifiers.None or InputModifiers.Shift)
            {
                key.Handled = true;
                MoveLocalTabFocus(input.Value.Modifiers == InputModifiers.Shift ? -1 : 1);
            }
            return;
        }
        key.Handled = true;
        if (result.Action is { } action) DispatchKeyboard(action);
        else RefreshStatus();
    }

    private void HandleMouse(object? sender, Mouse mouse)
    {
        if (_stopping || _menu.Active || !ReferenceEquals(_app.TopRunnableView, _workspaceWindow)) return;
        int x = mouse.ScreenPosition.X;
        int y = mouse.ScreenPosition.Y;
        if (state.ResizingSplitter is not null)
        {
            state.UpdateResize(x, _app.Screen.Width, _app.Screen.Height);
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased) || mouse.IsSingleDoubleOrTripleClicked)
            {
                state.EndResize();
                _suppressResizeClick = !mouse.IsSingleDoubleOrTripleClicked;
            }
            mouse.Handled = true;
            Refresh();
            return;
        }
        if (_suppressResizeClick && (mouse.IsSingleDoubleOrTripleClicked || mouse.Flags.HasFlag(MouseFlags.LeftButtonReleased)))
        {
            if (mouse.IsSingleDoubleOrTripleClicked) _suppressResizeClick = false;
            mouse.Handled = true;
            return;
        }
        if (mouse.Flags.HasFlag(MouseFlags.LeftButtonPressed))
        {
            _suppressResizeClick = false;
            if (state.BeginResize(x, y, _app.Screen.Width, _app.Screen.Height))
            {
                mouse.Handled = true;
                RefreshStatus();
            }
        }
    }

    private void MoveLocalTabFocus(int direction)
    {
        if (state.Focus == WorkspacePanel.Center && state.FocusArea == PanelFocusArea.Tabs)
        {
            state.Dispatch(direction > 0 ? WorkspaceCommand.NextTab : WorkspaceCommand.PreviousTab);
            Refresh();
            return;
        }
        View[] candidates = state.Focus switch
        {
            WorkspacePanel.Explorer => [_explorer],
            WorkspacePanel.Visualizer => [_visualizer, _dockAction, _dockLeft, _dockRight],
            _ when state.FocusArea == PanelFocusArea.Tabs => [_chatTab, _codeTab, _visualizerTab],
            _ when state.ActiveTab == WorkspaceTab.Chat => _chatComposer.Visible ? [_chatInput, _chat] : [_chat],
            _ when state.ActiveTab == WorkspaceTab.Code => [_code],
            _ => [_visualizer, _dockAction, _dockLeft, _dockRight]
        };
        View[] stops = candidates.Where(view => view.Visible && view.CanFocus).ToArray();
        if (stops.Length == 0) return;
        int current = Array.FindIndex(stops, view => view.HasFocus);
        int next = current < 0 ? direction > 0 ? 0 : stops.Length - 1 : (current + direction + stops.Length) % stops.Length;
        stops[next].SetFocus();
    }

    private void ResizeFocusedPanel(int delta)
    {
        state.ResizeFocusedPanel(delta, _app.Screen.Width, _app.Screen.Height);
        Refresh();
    }

    private void Execute(WorkspaceCommand command)
    {
        state.Dispatch(command);
        if (state.QuitRequested) { _app.RequestStop(); return; }
        Refresh();
    }

    private void Focus(WorkspacePanel panel)
    {
        state.SetFocus(panel);
        Refresh();
    }

    private void SendChat()
    {
        if (!state.Chat.Send()) return;
        _chatInput.Text = state.Chat.Draft;
        _chatThinkingStarted = Environment.TickCount64;
        _chatThinkingFrame = 0;
        state.SetStatus("Demo: Thinking...");
        RefreshStatus();
        _chat.MoveEnd();
        _chatInput.SetFocus();
    }

    private void PositionChatInput()
    {
        int panelHeight = _lastLayout?.Center.Height ?? 12;
        int width = Math.Max(1, (_lastLayout?.Center.Width ?? 30) - 10);
        int lines = state.Chat.Draft.Split('\n').Sum(line => Math.Max(1,
            (Terminal.Gui.Text.StringExtensions.GetColumns(line) + width - 1) / width));
        // One terminal row of padding above and below; grow with the draft,
        // leaving space for history before the editor starts scrolling.
        int maximum = Math.Max(3, Math.Min(panelHeight / 2, panelHeight - (panelHeight < 7 ? 4 : 8)));
        int height = Math.Min(maximum - 2, lines);
        int composerHeight = height + 2;
        _chatComposer.Height = composerHeight;
        _chatComposer.Y = Pos.AnchorEnd(composerHeight + 1);
        _chat.Height = Dim.Fill(composerHeight + 2);
        _chatInput.Y = 1;
        _chatInput.Height = height;
        _chatPromptMarker.Y = _chatInput.Y;
    }

    private void RefreshChatTranscript()
    {
        int width = Math.Max(1, (_lastLayout?.Center.Width ?? 30) - 6);
        var rendering = (width, state.Chat.Messages.Count, state.Chat.IsThinking, state.Chat.IsThinking ? _chatThinkingFrame : 0);
        if (_chatRendering == rendering) return;
        _chatRendering = rendering;
        _chat.Text = ChatPreviewTranscript.Render(state.Chat, width, capabilities.Unicode, _chatThinkingFrame);
        _chat.MoveEnd();
    }

    private void ShowProjectRoot() => MessageBox.Query(_app, "Project root", TerminalText.Sanitize(state.ProjectRoot), "OK");

    private void ActivateSelection()
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        int generation = ++_loadGeneration;
        if (state.ToggleExpandedSelection()) { Refresh(); return; }
        _loadCancellation = new();
        state.SetStatus("Opening selection…");
        RefreshStatus();
        _loadTask = LoadAsync(generation, _loadCancellation.Token);
    }

    private async Task LoadAsync(int generation, CancellationToken token)
    {
        try
        {
            SelectionResult? result = await state.LoadSelectionAsync(files, token).ConfigureAwait(false);
            if (_stopping || token.IsCancellationRequested) return;
            _app.Invoke(() =>
            {
                if (_stopping || generation != _loadGeneration || result is null) return;
                state.Apply(result);
                Refresh();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!_stopping) _app.Invoke(() => { state.SetStatus(ex.Message); RefreshStatus(); });
        }
    }

    private void Refresh()
    {
        if (_refreshing || _stopping) return;
        _refreshing = true;
        try
        {
            if (!_renderedRows.SequenceEqual(state.Rows) || _renderedExpandedCount != state.ExpandedDirectories.Count)
            {
                _explorer.SetSource(new ObservableCollection<string>(state.Rows.Select(row =>
                {
                    string marker = row.Entry.IsDirectory
                        ? (state.ExpandedDirectories.Contains(row.Entry.Path) ? (capabilities.Unicode ? "▾ " : "v ") : (capabilities.Unicode ? "▸ " : "> "))
                        : "  ";
                    return " " + new string(' ', row.Depth * 2) + marker + SingleLine(row.Entry.Name) + (row.Entry.IsLink ? " @" : "");
                })));
                _renderedRows = state.Rows.ToArray();
                _renderedExpandedCount = state.ExpandedDirectories.Count;
            }
            _explorer.SelectedItem = state.Rows.Count == 0 ? null : state.SelectedIndex;
            if (!ReferenceEquals(_renderedPreview, state.Preview) && state.Preview is { } preview)
            {
                _code.Text = preview.Error ?? preview.Content + (preview.Truncated ? "\n\n[Preview truncated]" : "");
                _renderedPreview = preview;
            }
            _chat.Visible = state.ActiveTab == WorkspaceTab.Chat;
            _code.Visible = state.ActiveTab == WorkspaceTab.Code;
            _codeCanvas.Visible = state.ActiveTab == WorkspaceTab.Code;
            _chatComposer.Visible = state.ActiveTab == WorkspaceTab.Chat && _lastLayout is { Center.Height: >= 5 };
            RefreshLayout();
            View tab = state.ActiveTab switch
            {
                WorkspaceTab.Chat => _chatComposer.Visible ? _chatInput : _chat,
                WorkspaceTab.Code => _code,
                _ => _visualizer
            };
            View focused = state.Focus switch
            {
                WorkspacePanel.Explorer => _explorer,
                WorkspacePanel.Center => state.FocusArea == PanelFocusArea.Tabs ? ActiveTabControl() : tab,
                _ => _visualizer
            };
            focused.SetFocus();
            RefreshStatus();
        }
        finally { _refreshing = false; }
    }

    private void RefreshLayout()
    {
        if (_stopping) return;
        WorkspaceLayout layout = WorkspaceLayout.Calculate(_app.Screen.Width, _app.Screen.Height, state.Focus, state.LayoutOptions);
        if (layout == _lastLayout && _lastActiveTab == state.ActiveTab) return;
        _lastLayout = layout;
        _lastActiveTab = state.ActiveTab;
        SetBounds(_explorerPanel, layout.Explorer);
        SetBounds(_centerPanel, layout.Center);
        bool tabbed = state.LayoutOptions.VisualizerPlacement == VisualizerPlacement.Tab;
        PanelBounds mapBounds = tabbed && state.ActiveTab == WorkspaceTab.Visualizer
            ? new(layout.Center.X, layout.Center.Y + 3, layout.Center.Width, Math.Max(0, layout.Center.Height - 3))
            : tabbed ? default : layout.Visualizer;
        SetBounds(_visualizerPanel, mapBounds);
        SetBounds(_explorerGrip, layout.ExplorerSplitter);
        SetBounds(_visualizerGrip, layout.VisualizerSplitter);
        bool tall = _app.Screen.Height >= 12;
        _appBar.Height = tall ? 3 : 1;
        _brand.Y = _projectName.Y = _menu.Y = tall ? 1 : 0;
        _brand.Visible = _app.Screen.Width >= 30;
        _projectName.Visible = _app.Screen.Width >= 65;
        _menu.X = _app.Screen.Width >= 30 ? Pos.AnchorEnd(25) : 0;
        _keyHints.Visible = tall;
        _status.Y = Math.Max(0, _app.Screen.Height - (tall ? 2 : 1));
        _keyHints.Y = Math.Max(0, _app.Screen.Height - 1);
        bool shortBody = Math.Max(layout.Explorer.Height, Math.Max(layout.Center.Height, layout.Visualizer.Height)) < 7;
        _filesHeading.Y = shortBody ? 0 : 1;
        _chatTab.Y = _codeTab.Y = _visualizerTab.Y = shortBody ? 0 : 1;
        _explorer.Y = shortBody ? 1 : 4;
        _chat.Y = shortBody ? 1 : 5;
        _chatComposer.Visible = state.ActiveTab == WorkspaceTab.Chat && layout.Center.Height >= 5;
        PositionChatInput();
        _codeCanvas.Y = shortBody ? 1 : 5;
        bool tightCode = layout.Center.Height - (shortBody ? 1 : 5) - 1 < 4;
        _code.Y = tightCode ? 0 : 1;
        _code.Height = Dim.Fill(tightCode ? 0 : 1);
        bool shortMap = mapBounds.Height < 14;
        int graphY = mapBounds.Height < 4 ? 0 : mapBounds.Height < 5 ? 1 : mapBounds.Height < 7 ? 3 : mapBounds.Height < 11 ? 4 : 6;
        _visualizer.Y = graphY;
        _graphWidth = Math.Max(0, mapBounds.Width - 4);
        _graphHeight = Math.Max(0, mapBounds.Height - graphY - 1);
        _mapHeading.Y = mapBounds.Height < 7 ? 0 : 1;
        _mapHeading.Visible = mapBounds.Height >= 4;
        _dockAction.Y = mapBounds.Height < 9 ? 2 : 3;
        _mapTitle.Y = 5;
        _mapTitle.Visible = mapBounds.Height >= 11;
        _filesCaption.Visible = !shortBody;
        _mapCaption.Visible = mapBounds.Height >= 11;
        _context.Visible = !shortBody && state.ActiveTab != WorkspaceTab.Visualizer;
        _dockAction.Visible = mapBounds.Height >= 5;
        _dockLeft.Visible = _dockRight.Visible = !shortMap;
    }

    private static void SetBounds(View view, PanelBounds bounds)
    {
        view.Visible = bounds.Width > 0 && bounds.Height > 0;
        view.X = bounds.X; view.Y = bounds.Y;
        view.Width = bounds.Width; view.Height = bounds.Height;
    }

    private void RefreshStatus()
    {
        ApplyPanelPresentation();
        RefreshChatTranscript();
        string indicator = capabilities.Unicode ? "● " : "* ";
        bool tabbed = state.LayoutOptions.VisualizerPlacement == VisualizerPlacement.Tab;
        bool centerFocused = state.Focus == WorkspacePanel.Center;
        bool mapFocused = tabbed ? centerFocused : state.Focus == WorkspacePanel.Visualizer;
        _mapTitle.Text = "Project map / DEMO / " + state.Graph.Zoom;
        if (_visualizerPanel.Visible) _visualizer.Update(_graphWidth, _graphHeight, mapFocused, sidebar: !tabbed);
        _filesHeading.Text = (state.Focus == WorkspacePanel.Explorer ? indicator : "  ") + "Files";
        _mapHeading.Text = (mapFocused ? indicator : "  ") + "Visualizer";
        _filesHeading.SetScheme(_theme.Heading(state.Focus == WorkspacePanel.Explorer));
        _mapHeading.SetScheme(_theme.Heading(mapFocused, content: tabbed));
        _mapCaption.Text = tabbed ? "Central tab / fictitious graph" : $"Dock {state.LayoutOptions.VisualizerPlacement.ToString().ToLowerInvariant()} / DEMO";
        _dockAction.Text = tabbed ? "Dock again" : "Open as tab";
        _dockAction.SetScheme(_theme.Tab(false, mapFocused, sidebar: !tabbed));
        _dockLeft.SetScheme(_theme.Tab(state.LayoutOptions.VisualizerPlacement == VisualizerPlacement.Left, mapFocused, sidebar: !tabbed));
        _dockRight.SetScheme(_theme.Tab(state.LayoutOptions.VisualizerPlacement == VisualizerPlacement.Right, mapFocused, sidebar: !tabbed));
        _filesCaption.Text = $"{state.Rows.Count} visible items";
        bool headerFocused = centerFocused && state.FocusArea == PanelFocusArea.Tabs;
        _chatTab.SetScheme(_theme.Tab(state.ActiveTab == WorkspaceTab.Chat, centerFocused));
        _codeTab.SetScheme(_theme.Tab(state.ActiveTab == WorkspaceTab.Code, centerFocused));
        _visualizerTab.SetScheme(_theme.Tab(state.ActiveTab == WorkspaceTab.Visualizer, centerFocused));
        // Keep the marker below the text, with its own softer color. On very
        // short panels the next row belongs to content; the status still names the focus area.
        _tabFocusAccent.Visible = headerFocused && _lastLayout is { Center.Height: >= 7 };
        if (_tabFocusAccent.Visible)
        {
            View activeTab = ActiveTabControl();
            int width = state.ActiveTab == WorkspaceTab.Visualizer ? 14 : 8;
            _tabFocusAccent.X = Pos.Left(activeTab);
            _tabFocusAccent.Y = Pos.Bottom(activeTab);
            _tabFocusAccent.Width = width;
            // An overline sits near the top of the next cell, leaving only
            // the font's small top bearing below the tab background.
            _tabFocusAccent.Text = new string(capabilities.Unicode ? '‾' : '-', width);
            _tabFocusAccent.SetScheme(_theme.TabFocusAccent);
        }
        _chatTab.Text = state.ActiveTab == WorkspaceTab.Chat ? indicator + "Chat" : "Chat";
        _codeTab.Text = state.ActiveTab == WorkspaceTab.Code ? indicator + "Code" : "Code";
        _visualizerTab.Text = state.ActiveTab == WorkspaceTab.Visualizer ? indicator + "Visualizer" : "Visualizer";
        _explorerGrip.Text = _visualizerGrip.Text = ":";
        _explorerGrip.SetScheme(state.ResizingSplitter == WorkspaceSplitter.Explorer ? _theme.Brand : _theme.Caption);
        _visualizerGrip.SetScheme(state.ResizingSplitter == WorkspaceSplitter.Visualizer ? _theme.Brand : _theme.Caption);
        _context.Text = headerFocused ? "Tabs: " + _keyboard.Hint(KeyboardAction.PreviousTab) + " / " + _keyboard.Hint(KeyboardAction.NextTab)
            + "   " + _keyboard.Hint(KeyboardAction.ConfirmTab) + " → Content" : state.ActiveTab == WorkspaceTab.Chat ? "Demo chat  /  Enter to send  /  Shift+Enter for a new line" :
            state.Preview is { } preview ? "Read only  /  " + SingleLine(Path.GetRelativePath(state.ProjectRoot, preview.Path)) : "Source preview";
        string separator = capabilities.Unicode ? "  ·  " : "  /  ";
        string focused = state.Focus switch
        {
            WorkspacePanel.Explorer => "Files",
            WorkspacePanel.Center => state.ActiveTab.ToString(),
            _ => "Visualizer"
        };
        string mode = state.Focus == WorkspacePanel.Center ? " " + state.FocusArea.ToString().ToLowerInvariant() : "";
        string text = focused + mode + separator + state.Status + separator + cadence.Description;
        _status.Text = SingleLine(capabilities.Unicode ? text : text.Replace('·', '/').Replace('…', '.'));
        _keyHints.Text = _keyboard.LeaderPending ? "Leader: " + _keyboard.Settings.Super + " — command or Esc to cancel"
            : mapFocused && state.FocusArea == PanelFocusArea.Content
                ? "Arrows  " + (state.Graph.SelectedMember is null ? "Cards" : "Members") + "   "
                    + _keyboard.Hint(KeyboardAction.GraphEnterComponent) + "  Select   "
                    + _keyboard.Hint(KeyboardAction.GraphOptions) + "  Options   "
                    + _keyboard.Hint(KeyboardAction.GraphExitComponent) + "  Back"
            : "Super=" + _keyboard.Settings.Super + "   " + _keyboard.Hint(KeyboardAction.NextPanel) + "  Panels   " + _keyboard.Hint(KeyboardAction.ToggleTabFocus)
                + "  Tabs/content   " + _keyboard.Hint(KeyboardAction.EditKeybindings) + "  Keybindings";
        if (_lastLayout is { } layout)
        {
            PanelBounds focusedBounds = state.Focus switch
            {
                WorkspacePanel.Explorer => layout.Explorer,
                WorkspacePanel.Center => layout.Center,
                _ => layout.Visualizer
            };
            _focusAccent.Update(focusedBounds);
        }
    }

    private void ApplyPanelPresentation()
    {
        var appearance = (state.Focus, state.LayoutOptions.VisualizerPlacement);
        if (_panelAppearance == appearance) return;
        _panelAppearance = appearance;
        bool filesFocused = state.Focus == WorkspacePanel.Explorer;
        bool centerFocused = state.Focus == WorkspacePanel.Center;
        bool tabbed = state.LayoutOptions.VisualizerPlacement == VisualizerPlacement.Tab;
        bool mapFocused = tabbed ? centerFocused : state.Focus == WorkspacePanel.Visualizer;
        ApplyScheme(_explorerPanel, _theme.Panel(true, filesFocused));
        ApplyScheme(_centerPanel, _theme.Panel(false, centerFocused));
        ApplyScheme(_visualizerPanel, _theme.Panel(!tabbed, mapFocused));
        _explorer.SetScheme(_theme.Explorer(filesFocused));
        _chat.SetScheme(_theme.PanelText(false, centerFocused, muted: true));
        ApplyScheme(_codeCanvas, _theme.CodeCanvas(centerFocused));
        ApplyScheme(_chatComposer, _theme.ChatInput(centerFocused));
        _chatPromptMarker.SetScheme(_theme.ChatPromptMarker(centerFocused));
        _visualizer.SetScheme(_theme.PanelText(!tabbed, mapFocused, muted: true));
        _filesCaption.SetScheme(_theme.PanelText(true, filesFocused, muted: true));
        _mapCaption.SetScheme(_theme.PanelText(!tabbed, mapFocused, muted: true));
        _context.SetScheme(_theme.PanelText(false, centerFocused, muted: true));
        _mapTitle.SetScheme(_theme.PanelText(!tabbed, mapFocused, bold: true));
    }

    private View ActiveTabControl() => state.ActiveTab switch
    { WorkspaceTab.Chat => _chatTab, WorkspaceTab.Code => _codeTab, _ => _visualizerTab };

    private View ContentControl() => state.Focus == WorkspacePanel.Center && state.ActiveTab == WorkspaceTab.Chat && _chatInput.HasFocus
        ? _chatInput : ActiveContentView();

    private void SetGraphStatus() => state.SetStatus("Graph: " + state.Graph.Selected.Name
        + (state.Graph.SelectedMember is { } member ? "." + member.Name + " / Members" : "")
        + " / " + state.Graph.Zoom + " / DEMO");

    private void OpenGraphComponent(bool options)
    {
        if (_app.Screen.Width < 45 || _app.Screen.Height < 16)
        { state.SetStatus("Enlarge the terminal to 45x16 to inspect a component"); RefreshStatus(); return; }
        PreviewGraphTarget target = state.Graph.Target;
        PreviewComponentAction action = options ? GraphPreviewDialogs.Options(_app, target) : PreviewComponentAction.Details;
        if (action == PreviewComponentAction.Details) GraphPreviewDialogs.Details(_app, state.Graph, target);
        else if (action == PreviewComponentAction.Chat) GraphPreviewDialogs.Chat(_app, target, state.Graph.ChatFor(target.Id), capabilities.Unicode);
        SetGraphStatus();
        Refresh();
    }

#pragma warning disable CS0618
    private View ActiveContentView() => state.Focus == WorkspacePanel.Visualizer ? _visualizer : state.ActiveTab switch
    { WorkspaceTab.Chat => _chat, WorkspaceTab.Code => _code, _ => _visualizer };
#pragma warning restore CS0618

    private void SelectTab(WorkspaceCommand command)
    {
        state.Dispatch(command);
        state.SetFocus(WorkspacePanel.Center, PanelFocusArea.Tabs);
        Refresh();
    }

    private void EditKeybindings()
    {
        if (_app.Screen.Width < 80 || _app.Screen.Height < 22)
        { state.SetStatus("Enlarge the terminal to 80x22 to edit keybindings"); RefreshStatus(); return; }
        _menu.HideActiveItem();
        _keyboard.CancelLeader();
        KeybindingsDialog.Show(_app, _keyboard, keybindingsPath, router => _keyboard = router);
        Refresh();
    }

    private void DispatchKeyboard(KeyboardAction action)
    {
        bool graphActive = state.Focus == WorkspacePanel.Visualizer || state.Focus == WorkspacePanel.Center && state.ActiveTab == WorkspaceTab.Visualizer;
        if (graphActive && action is KeyboardAction.ContentUp or KeyboardAction.ContentDown or KeyboardAction.ContentLeft or KeyboardAction.ContentRight
            or KeyboardAction.GraphZoomIn or KeyboardAction.GraphZoomOut or KeyboardAction.GraphResetZoom
            or KeyboardAction.GraphEnterComponent or KeyboardAction.GraphExitComponent or KeyboardAction.GraphOptions)
        {
            switch (action)
            {
                case KeyboardAction.ContentUp: state.Graph.Move(0, -1); break;
                case KeyboardAction.ContentDown: state.Graph.Move(0, 1); break;
                case KeyboardAction.ContentLeft: state.Graph.Move(-1, 0); break;
                case KeyboardAction.ContentRight: state.Graph.Move(1, 0); break;
                case KeyboardAction.GraphZoomIn: state.Graph.ChangeZoom(1); break;
                case KeyboardAction.GraphZoomOut: state.Graph.ChangeZoom(-1); break;
                case KeyboardAction.GraphResetZoom: state.Graph.ResetZoom(); break;
                case KeyboardAction.GraphEnterComponent:
                    if (state.Graph.SelectedMember is not null) { OpenGraphComponent(false); return; }
                    state.Graph.EnterComponent(); break;
                case KeyboardAction.GraphExitComponent: state.Graph.ExitComponent(); break;
                case KeyboardAction.GraphOptions: OpenGraphComponent(true); return;
            }
            SetGraphStatus(); RefreshStatus(); return;
        }
        switch (action)
        {
            case KeyboardAction.OpenMenu: _menu.InvokeCommand(Command.Activate); return;
            case KeyboardAction.EditKeybindings: EditKeybindings(); return;
            case KeyboardAction.ResizeSmaller: ResizeFocusedPanel(-2); return;
            case KeyboardAction.ResizeLarger: ResizeFocusedPanel(2); return;
            case KeyboardAction.CancelResize:
                state.EndResize(cancel: true); _suppressResizeClick = true; Refresh(); return;
            case KeyboardAction.OpenSelection: ActivateSelection(); return;
            case KeyboardAction.FocusFiles: Focus(WorkspacePanel.Explorer); return;
            case KeyboardAction.ContentUp: ContentControl().InvokeCommand(Command.Up); return;
            case KeyboardAction.ContentDown: ContentControl().InvokeCommand(Command.Down); return;
            case KeyboardAction.ContentLeft: ContentControl().InvokeCommand(Command.Left); return;
            case KeyboardAction.ContentRight: ContentControl().InvokeCommand(Command.Right); return;
        }
        WorkspaceCommand command = action switch
        {
            KeyboardAction.Quit => WorkspaceCommand.Quit, KeyboardAction.ToggleDock => WorkspaceCommand.ToggleVisualizerDock,
            KeyboardAction.DockLeft => WorkspaceCommand.DockVisualizerLeft, KeyboardAction.DockRight => WorkspaceCommand.DockVisualizerRight,
            KeyboardAction.ResetSizes => WorkspaceCommand.ResetPanelSizes,
            KeyboardAction.NextPanel => WorkspaceCommand.NextPanel, KeyboardAction.PreviousPanel => WorkspaceCommand.PreviousPanel,
            KeyboardAction.FocusChat => WorkspaceCommand.ChatTab, KeyboardAction.FocusCode => WorkspaceCommand.CodeTab,
            KeyboardAction.FocusVisualizer => WorkspaceCommand.ShowVisualizer,
            KeyboardAction.ToggleTabFocus => WorkspaceCommand.ToggleTabFocus, KeyboardAction.NextTab => WorkspaceCommand.NextTab,
            KeyboardAction.PreviousTab => WorkspaceCommand.PreviousTab, KeyboardAction.ConfirmTab => WorkspaceCommand.ConfirmTab,
            KeyboardAction.ReturnToContent => WorkspaceCommand.ReturnToContent,
            KeyboardAction.MoveUp => WorkspaceCommand.MoveUp, KeyboardAction.MoveDown => WorkspaceCommand.MoveDown,
            KeyboardAction.Collapse => WorkspaceCommand.Collapse,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
        Execute(command);
    }

    private static InputKey? ToInputKey(Key key)
    {
        var bare = key.KeyCode & ~(Terminal.Gui.Drivers.KeyCode.CtrlMask | Terminal.Gui.Drivers.KeyCode.AltMask | Terminal.Gui.Drivers.KeyCode.ShiftMask);
        string name = bare.ToString().ToUpperInvariant() switch
        {
            "CURSORUP" => "UP", "CURSORDOWN" => "DOWN", "CURSORLEFT" => "LEFT", "CURSORRIGHT" => "RIGHT",
            "DELETECHAR" => "DELETE", "ESCAPE" => "ESC", var value => value
        };
        if ((uint)bare is >= 48 and <= 57) name = ((char)(uint)bare).ToString();
        if ((uint)bare == 43) name = "PLUS";
        if ((uint)bare == 45) name = "MINUS";
        InputModifiers modifiers = (key.IsCtrl ? InputModifiers.Ctrl : 0) | (key.IsAlt ? InputModifiers.Alt : 0) | (key.IsShift ? InputModifiers.Shift : 0);
        try { return InputKey.Parse(name) with { Modifiers = modifiers }; }
        catch (ArgumentException) { return null; }
    }

    private void SetRenderCadence(int? framesPerSecond)
    {
        cadence.SetManual(framesPerSecond);
        _autoRefreshRate = framesPerSecond is null;
        Application.MaximumIterationsPerSecond = (ushort)cadence.FramesPerSecond;
        RefreshStatus();
    }

    private async Task MonitorRefreshRateAsync(CancellationToken token)
    {
        try
        {
            using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                if (!_autoRefreshRate) continue;
                DisplayRefreshRateReading reading = await refreshRateProbe.ReadAsync(token).ConfigureAwait(false);
                if (_stopping || token.IsCancellationRequested) return;
                _app.Invoke(() =>
                {
                    if (_stopping || !cadence.Apply(reading)) return;
                    Application.MaximumIterationsPerSecond = (ushort)cadence.FramesPerSecond;
                    RefreshStatus();
                });
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
