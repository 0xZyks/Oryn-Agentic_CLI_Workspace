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
public sealed class TerminalWorkspace(WorkspaceState state, IWorkspaceFiles files, TerminalCapabilities capabilities)
{
    private readonly WorkspaceTheme _theme = new(capabilities.Color);
    private readonly ListView _explorer = new() { X = 2, Y = 4, Width = Dim.Fill(2), Height = Dim.Fill(1) };
    private readonly View _explorerPanel = new() { CanFocus = true };
    private readonly View _centerPanel = new() { CanFocus = true };
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
    private readonly Label _dockAction = FlatTab("Open as tab", 2);
    private readonly Label _dockLeft = FlatTab("Left", 2);
    private readonly Label _dockRight = FlatTab("Right", 10);
    private readonly Label _explorerGrip = new() { TextAlignment = Alignment.Center, VerticalTextAlignment = Alignment.Center };
    private readonly Label _visualizerGrip = new() { TextAlignment = Alignment.Center, VerticalTextAlignment = Alignment.Center };
    private readonly Label _context = new() { X = 2, Y = 3, Width = Dim.Fill(2), Height = 1 };
    private readonly Label _welcomeHeading = new() { X = 3, Y = 6, Text = "Explore. Understand. Build.", Width = Dim.Fill(3), Height = 1 };
    private readonly Label _mapTitle = new() { X = 2, Y = 5, Text = "Project map", Width = Dim.Fill(2), Height = 1 };
    // Read-only text stays inside the provisional adapter until editing is implemented.
#pragma warning disable CS0618
    private readonly TextView _chat = new() { X = 3, Y = 5, Width = Dim.Fill(3), Height = Dim.Fill(2), BorderStyle = LineStyle.None, ReadOnly = true, WordWrap = true, TabKeyAddsTab = false };
    private readonly TextView _code = new() { X = 2, Y = 5, Width = Dim.Fill(2), Height = Dim.Fill(1), BorderStyle = LineStyle.None, ReadOnly = true, WordWrap = false, TabKeyAddsTab = false };
    private readonly TextView _visualizer = new() { X = 2, Y = 5, Width = Dim.Fill(2), Height = Dim.Fill(1), BorderStyle = LineStyle.None, ReadOnly = true, WordWrap = true, TabKeyAddsTab = false };
#pragma warning restore CS0618
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

    public void Run()
    {
        // Restore after both the window and the library's application have been disposed.
        using TerminalModeGuard originalTerminal = TerminalModeGuard.Capture();
        using IApplication app = Application.Create();
        _app = app;
        using Window window = new() { BorderStyle = LineStyle.None, Width = Dim.Fill(), Height = Dim.Fill() };
        ConfigureViews(window);
        app.ScreenChanged += (_, _) => { state.EndResize(); Refresh(); };
        window.Initialized += (_, _) => Refresh();
        try
        {
            app.Init();
            app.Keyboard.KeyDown += HandleKey;
            app.Mouse.MouseEvent += HandleMouse;
            Refresh();
            app.Run(window);
        }
        finally
        {
            _stopping = true;
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
        _chat.Text = "Explore the project on the left.\nOpen a file to read its source.\n\nProject conversations will appear here.\nChat is coming next.";
        _code.Text = "Open a file\n\nSelect a file in the explorer\nand press Enter to read it here.";
        _visualizer.Text = "Connect code and ideas.\n\nYour project map\nis coming next.";
        _explorerPanel.Add(_filesHeading, _filesCaption, _explorer);
        _chatTab.Width = _codeTab.Width = 8;
        _visualizerTab.Width = 14;
        _dockAction.Y = 3;
        _dockAction.Width = 14;
        _dockLeft.Y = _dockRight.Y = 4;
        _dockLeft.Width = _dockRight.Width = 6;
        _visualizerPanel.Add(_mapHeading, _mapCaption, _dockAction, _dockLeft, _dockRight, _mapTitle, _visualizer);
        _centerPanel.Add(_chatTab, _codeTab, _visualizerTab, _context, _welcomeHeading, _chat, _code);

        _menu = new MenuBar([
            new MenuBarItem("_File", [
                new MenuItem("Project root", "Show opened directory", () => ShowProjectRoot(), null),
                new MenuItem("_Quit", "Close Oryn", () => Execute(WorkspaceCommand.Quit), Key.Q.WithCtrl)
            ]),
            new MenuBarItem("_View", [
                new MenuItem("_Files", "File explorer", () => Focus(WorkspacePanel.Explorer), Key.F1),
                new MenuItem("_Chat", "Project chat", () => Execute(WorkspaceCommand.ChatTab), Key.F2),
                new MenuItem("_Code", "Read-only preview", () => Execute(WorkspaceCommand.CodeTab), Key.F3),
                new MenuItem("_Visualizer", "Project map", () => Execute(WorkspaceCommand.ShowVisualizer), Key.F4),
                new MenuItem("Visualizer _tab", "Open in the center", () => Execute(WorkspaceCommand.VisualizerTab), null),
                new MenuItem("Dock _left", "Beside the explorer", () => Execute(WorkspaceCommand.DockVisualizerLeft), Key.CursorLeft.WithCtrl.WithShift),
                new MenuItem("Dock _right", "Right of the center", () => Execute(WorkspaceCommand.DockVisualizerRight), Key.CursorRight.WithCtrl.WithShift),
                new MenuItem("_Toggle dock", "Tab or last dock side", () => Execute(WorkspaceCommand.ToggleVisualizerDock), Key.D.WithCtrl),
                new MenuItem("Wider panel", "Grow focused panel", () => ResizeFocusedPanel(2), Key.CursorRight.WithCtrl),
                new MenuItem("Narrower panel", "Shrink focused panel", () => ResizeFocusedPanel(-2), Key.CursorLeft.WithCtrl),
                new MenuItem("Reset panel sizes", "Keep current dock side", () => Execute(WorkspaceCommand.ResetPanelSizes), Key.F8)
            ]),
            new MenuBarItem("_Help", [
                new MenuItem("_Keys", "Keyboard navigation", () => MessageBox.Query(_app, "Oryn", "Tab / Shift+Tab: switch panels\nUp / Down: select or scroll\nEnter / Right: open file or folder\nLeft: collapse folder\nF1-F4: choose view\nCtrl+D: visualizer tab / dock\nCtrl+Shift+Left / Right: dock side\nCtrl+Left / Right: narrower / wider panel\nDrag separators to resize; Esc cancels\nF8: reset panel sizes\nF10: menu\nCtrl+Q: quit", "OK"), null)
            ])
        ]) { X = Pos.AnchorEnd(25), Y = 1, Width = 25, Height = 1 };
        _appBar.Add(_brand, _projectName, _menu);
        window.Add(_appBar, _explorerPanel, _centerPanel, _visualizerPanel, _explorerGrip, _visualizerGrip, _status, _keyHints);

        _explorer.ValueChanged += (_, _) =>
        {
            if (_refreshing || _stopping) return;
            state.Select(_explorer.SelectedItem ?? 0);
        };
        _explorer.Accepting += (_, args) => { args.Handled = true; ActivateSelection(); };
        _chatTab.Accepting += (_, args) => { args.Handled = true; Execute(WorkspaceCommand.ChatTab); };
        _codeTab.Accepting += (_, args) => { args.Handled = true; Execute(WorkspaceCommand.CodeTab); };
        _visualizerTab.Accepting += (_, args) => { args.Handled = true; Execute(WorkspaceCommand.VisualizerTab); };
        _dockAction.Accepting += (_, args) => { args.Handled = true; Execute(WorkspaceCommand.ToggleVisualizerDock); };
        _dockLeft.Accepting += (_, args) => { args.Handled = true; Execute(WorkspaceCommand.DockVisualizerLeft); };
        _dockRight.Accepting += (_, args) => { args.Handled = true; Execute(WorkspaceCommand.DockVisualizerRight); };
        BindFocus(_explorer, WorkspacePanel.Explorer);
        BindFocus(_chat, WorkspacePanel.Center);
        BindFocus(_code, WorkspacePanel.Center);
        BindFocus(_visualizer, WorkspacePanel.Visualizer);

        BindFocus(_chatTab, WorkspacePanel.Center);
        BindFocus(_codeTab, WorkspacePanel.Center);
        BindFocus(_visualizerTab, WorkspacePanel.Center);
        BindFocus(_dockAction, WorkspacePanel.Visualizer);
        BindFocus(_dockLeft, WorkspacePanel.Visualizer);
        BindFocus(_dockRight, WorkspacePanel.Visualizer);
        ApplyScheme(window, _theme.Canvas);
        ApplyScheme(_appBar, _theme.AppBar);
        ApplyScheme(_explorerPanel, _theme.Sidebar);
        ApplyScheme(_visualizerPanel, _theme.Sidebar);
        ApplyScheme(_centerPanel, _theme.Content);
        _brand.SetScheme(_theme.Brand);
        _projectName.SetScheme(_theme.Breadcrumb);
        _explorer.SetScheme(_theme.Explorer);
        _chat.SetScheme(_theme.ContentMuted);
        _visualizer.SetScheme(_theme.SidebarMuted);
        _filesCaption.SetScheme(_theme.SidebarMuted);
        _mapCaption.SetScheme(_theme.SidebarMuted);
        _context.SetScheme(_theme.ContentMuted);
        _welcomeHeading.SetScheme(_theme.WelcomeHeading);
        _mapTitle.SetScheme(_theme.MapHeading);
        _status.SetScheme(_theme.Caption);
        _keyHints.SetScheme(_theme.Caption);
        _explorerGrip.SetScheme(_theme.Caption);
        _visualizerGrip.SetScheme(_theme.Caption);
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

    private void BindFocus(View view, WorkspacePanel panel) => view.HasFocusChanged += (_, _) =>
    {
        if (_refreshing || _stopping || !view.HasFocus) return;
        state.SetFocus(panel);
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
        if (key == Key.Q.WithCtrl || key == Key.C.WithCtrl)
        {
            key.Handled = true;
            Execute(WorkspaceCommand.Quit);
            return;
        }
        if (_menu.Active) return;
        if (key == Key.Esc && state.ResizingSplitter is not null)
        {
            state.EndResize(cancel: true);
            _suppressResizeClick = true;
            Refresh();
        }
        else if (key == Key.D.WithCtrl) Execute(WorkspaceCommand.ToggleVisualizerDock);
        else if (key == Key.CursorLeft.WithCtrl.WithShift) Execute(WorkspaceCommand.DockVisualizerLeft);
        else if (key == Key.CursorRight.WithCtrl.WithShift) Execute(WorkspaceCommand.DockVisualizerRight);
        else if (key == Key.CursorLeft.WithCtrl) ResizeFocusedPanel(-2);
        else if (key == Key.CursorRight.WithCtrl) ResizeFocusedPanel(2);
        else if (key == Key.Tab) Execute(WorkspaceCommand.NextPanel);
        else if (key == Key.Tab.WithShift) Execute(WorkspaceCommand.PreviousPanel);
        else if (key == Key.F1) Focus(WorkspacePanel.Explorer);
        else if (key == Key.F2) Execute(WorkspaceCommand.ChatTab);
        else if (key == Key.F3) Execute(WorkspaceCommand.CodeTab);
        else if (key == Key.F4) Execute(WorkspaceCommand.ShowVisualizer);
        else if (key == Key.F8) Execute(WorkspaceCommand.ResetPanelSizes);
        else if (state.Focus == WorkspacePanel.Explorer && key == Key.CursorUp) Execute(WorkspaceCommand.MoveUp);
        else if (state.Focus == WorkspacePanel.Explorer && key == Key.CursorDown) Execute(WorkspaceCommand.MoveDown);
        else if (state.Focus == WorkspacePanel.Explorer && key == Key.CursorLeft) Execute(WorkspaceCommand.Collapse);
        else if (state.Focus == WorkspacePanel.Explorer && (key == Key.Enter || key == Key.CursorRight)) ActivateSelection();
        else return;
        key.Handled = true;
    }

    private void HandleMouse(object? sender, Mouse mouse)
    {
        if (_stopping || _menu.Active) return;
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
            View tab = state.ActiveTab switch
            {
                WorkspaceTab.Chat => _chat,
                WorkspaceTab.Code => _code,
                _ => _visualizer
            };
            _chat.Visible = state.ActiveTab == WorkspaceTab.Chat;
            _code.Visible = state.ActiveTab == WorkspaceTab.Code;
            _welcomeHeading.Visible = state.ActiveTab == WorkspaceTab.Chat && _app.Screen.Height >= 16;
            RefreshLayout();
            View focused = state.Focus switch
            {
                WorkspacePanel.Explorer => _explorer,
                WorkspacePanel.Center => tab,
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
        ApplyScheme(_visualizerPanel, tabbed ? _theme.Content : _theme.Sidebar);
        _visualizer.SetScheme(tabbed ? _theme.ContentMuted : _theme.SidebarMuted);
        _mapCaption.SetScheme(tabbed ? _theme.ContentMuted : _theme.SidebarMuted);
        _mapTitle.SetScheme(tabbed ? _theme.WelcomeHeading : _theme.MapHeading);
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
        _explorer.Y = shortBody ? 2 : 4;
        _chat.Y = shortBody ? 2 : _app.Screen.Height >= 16 ? 8 : 5;
        _code.Y = shortBody ? 2 : 5;
        bool shortMap = mapBounds.Height < 14;
        _visualizer.Y = mapBounds.Height < 4 ? 0 : mapBounds.Height < 5 ? 1 : mapBounds.Height < 7 ? 3 :
            mapBounds.Height < 11 ? 4 : shortMap ? 7 : 8;
        _mapHeading.Y = mapBounds.Height < 7 ? 0 : 1;
        _mapHeading.Visible = mapBounds.Height >= 4;
        _dockAction.Y = mapBounds.Height < 9 ? 2 : 3;
        _mapTitle.Y = shortMap ? 5 : 6;
        _welcomeHeading.Visible = state.ActiveTab == WorkspaceTab.Chat && _app.Screen.Height >= 16 && !shortBody;
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
        string indicator = capabilities.Unicode ? "● " : "* ";
        _filesHeading.Text = (state.Focus == WorkspacePanel.Explorer ? indicator : "  ") + "Files";
        _mapHeading.Text = (state.Focus == WorkspacePanel.Visualizer ? indicator : "  ") + "Visualizer";
        _filesHeading.SetScheme(_theme.Heading(state.Focus == WorkspacePanel.Explorer));
        bool tabbed = state.LayoutOptions.VisualizerPlacement == VisualizerPlacement.Tab;
        _mapHeading.SetScheme(tabbed ? _theme.WelcomeHeading : _theme.Heading(state.Focus == WorkspacePanel.Visualizer));
        _mapCaption.Text = tabbed ? "Central tab" : $"Dock {state.LayoutOptions.VisualizerPlacement.ToString().ToLowerInvariant()}";
        _dockAction.Text = tabbed ? "Dock again" : "Open as tab";
        _dockAction.SetScheme(_theme.Tab(false));
        _dockLeft.SetScheme(_theme.Tab(state.LayoutOptions.VisualizerPlacement == VisualizerPlacement.Left));
        _dockRight.SetScheme(_theme.Tab(state.LayoutOptions.VisualizerPlacement == VisualizerPlacement.Right));
        _filesCaption.Text = $"{state.Rows.Count} visible items";
        _chatTab.SetScheme(_theme.Tab(state.ActiveTab == WorkspaceTab.Chat));
        _codeTab.SetScheme(_theme.Tab(state.ActiveTab == WorkspaceTab.Code));
        _visualizerTab.SetScheme(_theme.Tab(state.ActiveTab == WorkspaceTab.Visualizer));
        _chatTab.Text = state.ActiveTab == WorkspaceTab.Chat ? indicator + "Chat" : "Chat";
        _codeTab.Text = state.ActiveTab == WorkspaceTab.Code ? indicator + "Code" : "Code";
        _visualizerTab.Text = state.ActiveTab == WorkspaceTab.Visualizer ? indicator + "Visualizer" : "Visualizer";
        _explorerGrip.Text = _visualizerGrip.Text = ":";
        _explorerGrip.SetScheme(state.ResizingSplitter == WorkspaceSplitter.Explorer ? _theme.Brand : _theme.Caption);
        _visualizerGrip.SetScheme(state.ResizingSplitter == WorkspaceSplitter.Visualizer ? _theme.Brand : _theme.Caption);
        _context.Text = state.ActiveTab == WorkspaceTab.Chat ? "Project conversation" :
            state.Preview is { } preview ? "Read only  /  " + SingleLine(Path.GetRelativePath(state.ProjectRoot, preview.Path)) : "Source preview";
        string separator = capabilities.Unicode ? "  ·  " : "  /  ";
        string focused = state.Focus switch
        {
            WorkspacePanel.Explorer => "Files",
            WorkspacePanel.Center => state.ActiveTab.ToString(),
            _ => "Visualizer"
        };
        string text = focused + separator + state.Status;
        _status.Text = SingleLine(capabilities.Unicode ? text : text.Replace('·', '/').Replace('…', '.'));
        _keyHints.Text = "Tab  Panels   Ctrl+D  Dock/tab   Drag or Ctrl+Left/Right  Resize   F10  Menu   Ctrl+Q  Quit";
    }
}
