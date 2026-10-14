using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class WorkspaceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "oryn-tests-" + Guid.NewGuid());

    public WorkspaceTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void NoArgumentUsesCallerDirectory()
    {
        Assert.Equal(_root, StartupOptions.Parse([], _root).ProjectRoot);
        Assert.Equal(Path.Combine(_root, "child"), StartupOptions.Parse(["child"], _root).ProjectRoot);
    }

    [Fact]
    public void OptionsDoNotBecomeProjectPaths()
    {
        StartupOptions options = StartupOptions.Parse(["--ascii", "--check", "--no-color"], _root);
        Assert.Equal(_root, options.ProjectRoot);
        Assert.True(options.Check);
        Assert.False(options.UseUnicode);
        Assert.False(options.UseColor);
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--unknown"], _root));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["one", "two"], _root));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--config"], _root));
        Assert.Equal(Path.Combine(_root, "-folder"), StartupOptions.Parse(["--", "-folder"], _root).ProjectRoot);
    }

    [Fact]
    public void FocusAndTabsAreIndependentOfRenderer()
    {
        WorkspaceState state = new(_root);
        Assert.Equal(VisualizerPlacement.Tab, state.LayoutOptions.VisualizerPlacement);
        Assert.Equal(WorkspaceTab.Chat, state.ActiveTab);
        WorkspaceLayout initial = WorkspaceLayout.Calculate(120, 30, state.Focus, state.LayoutOptions);
        Assert.Equal(0, initial.Visualizer.Width);
        Assert.Equal(0, initial.VisualizerSplitter.Width);
        state.Dispatch(WorkspaceCommand.PreviousPanel);
        Assert.Equal(WorkspacePanel.Center, state.Focus);
        state.Dispatch(WorkspaceCommand.NextPanel);
        Assert.Equal(WorkspacePanel.Explorer, state.Focus);
        state.Dispatch(WorkspaceCommand.CodeTab);
        Assert.Equal(WorkspacePanel.Center, state.Focus);
        Assert.Equal(WorkspaceTab.Code, state.ActiveTab);
        state.Dispatch(WorkspaceCommand.Quit);
        Assert.True(state.QuitRequested);
    }

    [Theory]
    [InlineData(120, 30)]
    [InlineData(90, 12)]
    [InlineData(60, 20)]
    [InlineData(1, 1)]
    [InlineData(0, 0)]
    public void LayoutStaysWithinTerminal(int width, int height)
    {
        foreach (WorkspacePanel focus in Enum.GetValues<WorkspacePanel>())
        {
            WorkspaceLayout layout = WorkspaceLayout.Calculate(width, height, focus);
            foreach (PanelBounds bounds in new[] { layout.Explorer, layout.Center, layout.Visualizer })
            {
                Assert.InRange(bounds.Width, 0, width);
                Assert.InRange(bounds.Height, 0, height);
                Assert.True(bounds.X + bounds.Width <= width);
                if (bounds.Height > 0) Assert.True(bounds.Y + bounds.Height <= height);
            }
            if (layout.Compact && width > 0 && height > 2)
            {
                PanelBounds visible = layout.VisiblePanel switch
                {
                    WorkspacePanel.Explorer => layout.Explorer,
                    WorkspacePanel.Center => layout.Center,
                    _ => layout.Visualizer
                };
                Assert.Equal(layout.VisiblePanel, layout.HitTest(visible.X, visible.Y));
            }
        }
    }

    [Fact]
    public async Task ExplorerLoadsLazilyAndCollapsesDescendants()
    {
        string child = Directory.CreateDirectory(Path.Combine(_root, "child")).FullName;
        string nested = Directory.CreateDirectory(Path.Combine(child, "nested")).FullName;
        await File.WriteAllTextAsync(Path.Combine(nested, "hello.txt"), "hello");
        LocalWorkspaceFiles files = new(_root, new());
        WorkspaceState state = new(_root);
        state.LoadRoot(await files.ListAsync(_root, default));
        Assert.Single(state.Rows);
        state.Apply((await state.LoadSelectionAsync(files, default))!);
        Assert.Equal(2, state.Rows.Count);
        state.Select(1);
        state.Apply((await state.LoadSelectionAsync(files, default))!);
        Assert.Equal(3, state.Rows.Count);
        state.Select(2);
        state.Dispatch(WorkspaceCommand.Collapse);
        Assert.Equal(2, state.Rows.Count);
        Assert.Equal(1, state.SelectedIndex);
        state.Dispatch(WorkspaceCommand.Collapse);
        Assert.Single(state.Rows);
        Assert.Empty(state.ExpandedDirectories);
    }

    [Fact]
    public async Task FileActivationShowsPreviewWithoutChangingFiles()
    {
        string file = Path.Combine(_root, "hello.txt");
        await File.WriteAllTextAsync(file, "hello");
        LocalWorkspaceFiles files = new(_root, new());
        WorkspaceState state = new(_root);
        state.LoadRoot(await files.ListAsync(_root, default));
        state.Apply((await state.LoadSelectionAsync(files, default))!);
        Assert.Equal("hello", state.Preview?.Content);
        Assert.Equal(WorkspaceTab.Code, state.ActiveTab);
        Assert.Equal(WorkspacePanel.Center, state.Focus);
        Assert.Equal("hello", await File.ReadAllTextAsync(file));
    }

    [Fact]
    public async Task ListingsSortDirectoriesFirstAndHideDotFiles()
    {
        Directory.CreateDirectory(Path.Combine(_root, "z-dir"));
        await File.WriteAllTextAsync(Path.Combine(_root, "a.txt"), "hello");
        await File.WriteAllTextAsync(Path.Combine(_root, ".hidden"), "hello");
        DirectoryResult result = await new LocalWorkspaceFiles(_root, new()).ListAsync(_root, default);
        Assert.Equal(new[] { "z-dir", "a.txt" }, result.Entries.Select(e => e.Name));
        DirectoryResult limited = await new LocalWorkspaceFiles(_root, new() { ShowHiddenFiles = true, MaxDirectoryEntries = 1 }).ListAsync(_root, default);
        Assert.Single(limited.Entries);
        Assert.True(limited.Truncated);
    }

    [Fact]
    public async Task PreviewIsBoundedAndStripsTerminalControls()
    {
        string file = Path.Combine(_root, "preview.txt");
        await File.WriteAllTextAsync(file, "hello\u001b[31m" + new string('x', 500));
        FilePreview preview = await new LocalWorkspaceFiles(_root, new() { MaxPreviewCharacters = 256 }).ReadAsync(file, default);
        Assert.True(preview.Truncated);
        Assert.DoesNotContain('\u001b', preview.Content);
        await File.WriteAllBytesAsync(file, [1, 0, 2]);
        Assert.NotNull((await new LocalWorkspaceFiles(_root, new()).ReadAsync(file, default)).Error);
    }

    [Fact]
    public async Task OutOfRootAndMissingPathsProduceVisibleErrors()
    {
        LocalWorkspaceFiles files = new(_root, new());
        Assert.NotNull((await files.ListAsync(Path.GetDirectoryName(_root)!, default)).Error);
        Assert.NotNull((await files.ReadAsync(Path.Combine(_root, "missing"), default)).Error);
        Assert.NotNull((await files.ListAsync(Path.Combine(_root, "missing"), default)).Error);
    }

    [Fact]
    public async Task SymbolicLinksCannotEscapeRoot()
    {
        if (OperatingSystem.IsWindows()) return; // Creating symlinks may require privileges on Windows.
        string link = Path.Combine(_root, "outside");
        Directory.CreateSymbolicLink(link, Path.GetDirectoryName(_root)!);
        LocalWorkspaceFiles files = new(_root, new());
        Assert.True(Assert.Single((await files.ListAsync(_root, default)).Entries).IsLink);
        Assert.NotNull((await files.ListAsync(link, default)).Error);
        Assert.NotNull((await files.ReadAsync(Path.Combine(link, "anything"), default)).Error);
    }

    [Fact]
    public async Task CancelledOperationsDoNotLoadResults()
    {
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LocalWorkspaceFiles(_root, new()).ListAsync(_root, cancellation.Token));
    }

    [Fact]
    public async Task InvalidConfigurationIsRejected()
    {
        string config = Path.Combine(_root, "settings.json");
        await File.WriteAllTextAsync(config, "{\"MaxDirectoryEntries\":0}");
        Assert.Throws<InvalidDataException>(() => WorkspaceSettings.Load(config));
        await File.WriteAllTextAsync(config, "{\"UnknownSetting\":true}");
        Assert.Throws<System.Text.Json.JsonException>(() => WorkspaceSettings.Load(config));
        await File.WriteAllTextAsync(config, "{\"ShowHiddenFiles\":true}");
        Assert.True(WorkspaceSettings.Load(config).ShowHiddenFiles);
    }

    [Fact]
    public void VisualizerMovesBetweenTabAndEitherDockWithoutLosingContentTab()
    {
        WorkspaceState state = new(_root);
        state.Dispatch(WorkspaceCommand.CodeTab);
        state.Dispatch(WorkspaceCommand.VisualizerTab);
        Assert.Equal(VisualizerPlacement.Tab, state.LayoutOptions.VisualizerPlacement);
        Assert.Equal(WorkspaceTab.Visualizer, state.ActiveTab);
        Assert.Equal(WorkspacePanel.Center, state.Focus);
        state.Dispatch(WorkspaceCommand.DockVisualizerLeft);
        Assert.Equal(WorkspaceTab.Code, state.ActiveTab);
        Assert.Equal(WorkspacePanel.Visualizer, state.Focus);
        state.Dispatch(WorkspaceCommand.ToggleVisualizerDock);
        state.Dispatch(WorkspaceCommand.ToggleVisualizerDock);
        Assert.Equal(VisualizerPlacement.Left, state.LayoutOptions.VisualizerPlacement);
        state.Dispatch(WorkspaceCommand.DockVisualizerRight);
        Assert.Equal(VisualizerPlacement.Right, state.LayoutOptions.VisualizerPlacement);
        Assert.Equal(WorkspaceTab.Code, state.ActiveTab);
    }

    [Fact]
    public void TabbedVisualizerDoesNotIntroduceAnInvisibleFocusStop()
    {
        WorkspaceState state = new(_root);
        state.Dispatch(WorkspaceCommand.VisualizerTab);
        state.Dispatch(WorkspaceCommand.NextPanel);
        Assert.Equal(WorkspacePanel.Explorer, state.Focus);
        state.Dispatch(WorkspaceCommand.NextPanel);
        Assert.Equal(WorkspacePanel.Center, state.Focus);
        state.Dispatch(WorkspaceCommand.NextPanel);
        Assert.Equal(WorkspacePanel.Explorer, state.Focus);
        state.SetFocus(WorkspacePanel.Visualizer);
        Assert.Equal(WorkspacePanel.Center, state.Focus);
        Assert.Equal(WorkspaceTab.Visualizer, state.ActiveTab);
        state.SetVisualizerPlacement(VisualizerPlacement.Left);
        state.SetFocus(WorkspacePanel.Explorer);
        state.Dispatch(WorkspaceCommand.NextPanel);
        Assert.Equal(WorkspacePanel.Visualizer, state.Focus);
        state.Dispatch(WorkspaceCommand.NextPanel);
        Assert.Equal(WorkspacePanel.Center, state.Focus);
    }

    [Theory]
    [InlineData(90)]
    [InlineData(120)]
    [InlineData(200)]
    public void DockLayoutsRespectOrderingMinimumSizesAndSplitterHitTesting(int width)
    {
        foreach (VisualizerPlacement placement in Enum.GetValues<VisualizerPlacement>())
        foreach (int? preferred in new int?[] { null, 18, 60, 9999 })
        {
            WorkspaceLayout layout = WorkspaceLayout.Calculate(width, 30, WorkspacePanel.Center,
                new() { VisualizerPlacement = placement, ExplorerWidth = preferred, VisualizerWidth = preferred });
            Assert.True(layout.Center.Width >= WorkspaceLayout.MinCenterWidth);
            Assert.True(layout.Explorer.Width >= WorkspaceLayout.MinExplorerWidth);
            Assert.Equal(WorkspaceSplitter.Explorer, layout.HitTestSplitter(layout.ExplorerSplitter.X, layout.ExplorerSplitter.Y));
            PanelBounds[] visible = placement == VisualizerPlacement.Tab
                ? [layout.Explorer, layout.Center] : [layout.Explorer, layout.Center, layout.Visualizer];
            foreach (PanelBounds bounds in visible) Assert.True(bounds.X + bounds.Width <= width);
            PanelBounds[] ordered = visible.OrderBy(bounds => bounds.X).ToArray();
            for (int i = 1; i < ordered.Length; i++) Assert.True(ordered[i - 1].X + ordered[i - 1].Width < ordered[i].X);
            if (placement == VisualizerPlacement.Tab) Assert.Equal(0, layout.Visualizer.Width);
            else
            {
                Assert.True(layout.Visualizer.Width >= WorkspaceLayout.MinVisualizerWidth);
                Assert.Equal(placement == VisualizerPlacement.Left, layout.Visualizer.X < layout.Center.X);
                Assert.Equal(WorkspaceSplitter.Visualizer, layout.HitTestSplitter(layout.VisualizerSplitter.X, layout.VisualizerSplitter.Y));
            }
        }
    }

    [Fact]
    public void DragClampsOnlyTheSelectedPanelAndCanBeCancelled()
    {
        WorkspaceState state = new(_root);
        WorkspaceLayout original = WorkspaceLayout.Calculate(120, 30, state.Focus, state.LayoutOptions);
        Assert.False(state.BeginResize(original.Explorer.X, original.Explorer.Y, 120, 30));
        Assert.True(state.BeginResize(original.ExplorerSplitter.X, original.ExplorerSplitter.Y, 120, 30));
        state.UpdateResize(1000, 120, 30);
        WorkspaceLayout resized = WorkspaceLayout.Calculate(120, 30, state.Focus, state.LayoutOptions);
        Assert.Equal(WorkspaceLayout.MinCenterWidth, resized.Center.Width);
        Assert.Equal(original.Visualizer.Width, resized.Visualizer.Width);
        state.UpdateResize(-1000, 120, 30);
        Assert.Equal(WorkspaceLayout.MinExplorerWidth, WorkspaceLayout.Calculate(120, 30, state.Focus, state.LayoutOptions).Explorer.Width);
        state.EndResize(cancel: true);
        Assert.Null(state.ResizingSplitter);
        Assert.Null(state.LayoutOptions.ExplorerWidth);
        Assert.Equal(original.Explorer, WorkspaceLayout.Calculate(120, 30, state.Focus, state.LayoutOptions).Explorer);
    }

    [Theory]
    [InlineData(VisualizerPlacement.Left, 4)]
    [InlineData(VisualizerPlacement.Right, -4)]
    public void VisualizerDragDirectionFollowsDockSide(VisualizerPlacement placement, int expectedChange)
    {
        WorkspaceState state = new(_root);
        state.SetVisualizerPlacement(placement);
        WorkspaceLayout before = WorkspaceLayout.Calculate(120, 30, state.Focus, state.LayoutOptions);
        Assert.True(state.BeginResize(before.VisualizerSplitter.X, before.VisualizerSplitter.Y, 120, 30));
        state.UpdateResize(before.VisualizerSplitter.X + 4, 120, 30);
        state.EndResize();
        WorkspaceLayout after = WorkspaceLayout.Calculate(120, 30, state.Focus, state.LayoutOptions);
        Assert.Equal(before.Visualizer.Width + expectedChange, after.Visualizer.Width);
        Assert.Equal(before.Explorer.Width, after.Explorer.Width);
    }

    [Fact]
    public void ManualWidthsSurviveTerminalResizeAndResetKeepsDockSide()
    {
        WorkspaceState state = new(_root);
        state.ResizeFocusedPanel(10, 120, 30);
        int? preferred = state.LayoutOptions.ExplorerWidth;
        Assert.Equal(34, preferred);
        state.ResizeFocusedPanel(2, 60, 18);
        Assert.Equal(preferred, state.LayoutOptions.ExplorerWidth);
        Assert.Equal(34, WorkspaceLayout.Calculate(120, 30, state.Focus, state.LayoutOptions).Explorer.Width);
        state.SetVisualizerPlacement(VisualizerPlacement.Left);
        state.ResetPanelSizes();
        Assert.Null(state.LayoutOptions.ExplorerWidth);
        Assert.Equal(VisualizerPlacement.Left, state.LayoutOptions.VisualizerPlacement);
        state.Dispatch(WorkspaceCommand.CodeTab);
        WorkspaceLayout before = WorkspaceLayout.Calculate(120, 30, state.Focus, state.LayoutOptions);
        state.ResizeFocusedPanel(4, 120, 30);
        Assert.Equal(before.Center.Width + 4, WorkspaceLayout.Calculate(120, 30, state.Focus, state.LayoutOptions).Center.Width);
    }
}
