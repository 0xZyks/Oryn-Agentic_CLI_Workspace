using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class AppearanceSearchTests
{
    [Fact]
    public void AppearancePreviewCancelAndSavedRestartPreserveChat()
    {
        using Fixture fixture = new();
        using (GraphicalWorkspaceController ui = fixture.Create())
        {
            ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("unfinished draft");
            ui.OpenNavbarMenu(5); ui.Key(InputKey.Parse("Down"), 0); ui.Key(InputKey.Parse("Enter"), 0);
            Assert.Equal(WorkspaceOverlay.Appearance, ui.Overlay);
            ui.Key(InputKey.Parse("Ctrl+A"), 1); ui.Text("#32d2a1");
            Assert.Equal("#32D2A1", ui.DisplayAppearance.AccentColor);
            Assert.Equal("#B49AF2", ui.Appearance.AccentColor);
            ui.Key(InputKey.Parse("Esc"), 2);
            Assert.Equal("#B49AF2", ui.DisplayAppearance.AccentColor);
            Assert.False(File.Exists(fixture.AppearancePath));
            ui.OpenAppearance(); ui.Key(InputKey.Parse("Ctrl+A"), 3); ui.Text("#32D2A1");
            ui.Key(InputKey.Parse("Tab"), 4); Assert.Equal(1, ui.AppearanceTarget);
            ui.Key(InputKey.Parse("Ctrl+A"), 5); ui.Text("#E6E6EA");
            ui.Key(InputKey.Parse("Alt+S"), 6);
            Assert.Equal(WorkspaceOverlay.None, ui.Overlay);
            Assert.Equal("unfinished draft", ui.State.Chat.Draft); Assert.Empty(ui.State.Chat.Messages);
        }
        using GraphicalWorkspaceController restarted = fixture.Create();
        Assert.Equal("#32D2A1", restarted.Appearance.AccentColor);
        Assert.Equal("#E6E6EA", restarted.Appearance.ThemeColor);
    }

    [Fact]
    public void InvalidColorAndFailedSaveDoNotReplaceCommittedAppearance()
    {
        using Fixture fixture = new(); using GraphicalWorkspaceController ui = fixture.Create();
        ui.OpenAppearance(); ui.Key(InputKey.Parse("Ctrl+A"), 0); ui.Text("red;bad");
        Assert.False(ui.ApplyAppearance(true)); Assert.NotEmpty(ui.AppearanceError);
        Assert.Equal(new AppearanceSettings(), ui.Appearance); Assert.False(File.Exists(fixture.AppearancePath));
        ui.Key(InputKey.Parse("Ctrl+A"), 0); ui.Text("#99CCDD");
        Directory.CreateDirectory(fixture.AppearancePath); // File destination is deliberately unavailable.
        Assert.False(ui.ApplyAppearance(true)); Assert.Equal(new AppearanceSettings(), ui.Appearance);
        Assert.Equal(WorkspaceOverlay.Appearance, ui.Overlay);
        Assert.True(ui.ApplyAppearance(false)); Assert.Equal("#99CCDD", ui.Appearance.AccentColor);
    }

    [Fact]
    public void CorruptedPersonalAppearanceDoesNotPreventWorkspaceNavigation()
    {
        using Fixture fixture = new(); File.WriteAllText(fixture.AppearancePath, "{broken json");
        using GraphicalWorkspaceController ui = fixture.Create();
        Assert.Equal(new AppearanceSettings(), ui.Appearance);
        Assert.Contains("Appearance profile unavailable", ui.State.Status);
        ui.Execute(KeyboardAction.FocusFiles, 0); Assert.Equal(WorkspacePanel.Explorer, ui.State.Focus);
    }

    [Fact]
    public async Task SearchFindsUnexpandedFileNamesAndFunctionTextWithSourceLine()
    {
        using Fixture fixture = new(); Directory.CreateDirectory(Path.Combine(fixture.Root, "src"));
        File.WriteAllText(Path.Combine(fixture.Root, "src", "Worker.cs"), "class Worker\n{\n    void RunEngine() {}\n}\n");
        File.WriteAllText(Path.Combine(fixture.Root, "src", "Second.py"), "def RunEngine():\n    pass\n");
        WorkspaceSearchResult byName = await fixture.Files.SearchAsync("worker", default);
        Assert.Single(byName.Matches); Assert.Null(byName.Matches[0].Line);
        Assert.Equal(Path.Combine("src", "Worker.cs"), byName.Matches[0].RelativePath);
        WorkspaceSearchResult byText = await fixture.Files.SearchAsync("runengine", default);
        Assert.Equal(2, byText.Matches.Count);
        WorkspaceSearchMatch cs = byText.Matches.Single(m => m.Entry.Name == "Worker.cs");
        Assert.Equal(3, cs.Line); Assert.Contains("RunEngine", cs.Snippet);
        Assert.False(byText.Partial);
        using GraphicalWorkspaceController ui = fixture.Create();
        ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("chat draft");
        ui.Key(InputKey.Parse("Ctrl+F"), 0); ui.Text("RunEngine");
        Assert.True(ui.SearchFocused); Assert.Equal("chat draft", ui.State.Chat.Draft);
        await Wait(ui, () => !ui.Searching);
        int index = ui.SearchResult!.Matches.ToList().FindIndex(m => m.Entry.Name == "Worker.cs");
        ui.SelectSearchResult(index); await Wait(ui, () => !ui.Loading);
        Assert.Equal(cs.Entry.Path, ui.State.Preview!.Path); Assert.Equal(2, ui.CodeTop);
        Assert.Equal(WorkspaceTab.Code, ui.State.ActiveTab);
        ui.Key(InputKey.Parse("Ctrl+F"), 0); ui.Key(InputKey.Parse("Esc"), 0);
        Assert.False(ui.SearchActive); Assert.False(ui.SearchFocused);
        Assert.Equal("chat draft", ui.State.Chat.Draft);
    }

    [Fact]
    public async Task OpeningSearchResultRevalidatesLineAfterAnExternalEdit()
    {
        using Fixture fixture = new();
        string file = Path.Combine(fixture.Root, "changed.cs");
        File.WriteAllText(file, "void Target() {}\n");
        using GraphicalWorkspaceController ui = fixture.Create();
        ui.FocusSearch(); ui.Text("Target"); await Wait(ui, () => !ui.Searching);
        Assert.Equal(1, ui.SearchResult!.Matches[0].Line);
        File.WriteAllText(file, "// moved\n\nvoid Target() {}\n");
        ui.SelectSearchResult(0); await Wait(ui, () => !ui.Loading);
        Assert.Equal(3, ui.CodeMatchLine); Assert.Equal(2, ui.CodeTop);
        File.WriteAllText(file, "// removed\n");
        ui.SelectSearchResult(0); await Wait(ui, () => !ui.Loading);
        Assert.Null(ui.CodeMatchLine); Assert.Equal(0, ui.CodeTop);
        Assert.Contains("search text no longer present", ui.State.Status);
    }

    [Fact]
    public async Task SearchDoesNotFollowLinksOrScanGeneratedHiddenAndBinaryContent()
    {
        using Fixture fixture = new();
        foreach (string folder in new[] { "bin", "obj", ".git", ".hidden", "node_modules" })
        {
            Directory.CreateDirectory(Path.Combine(fixture.Root, folder));
            File.WriteAllText(Path.Combine(fixture.Root, folder, "skipped.cs"), "Needle");
        }
        File.WriteAllBytes(Path.Combine(fixture.Root, "binary.cs"), "Needle\0binary"u8.ToArray());
        string outside = fixture.Root + "-outside"; Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(outside, "escape.cs"), "Needle");
            if (!OperatingSystem.IsWindows()) Directory.CreateSymbolicLink(Path.Combine(fixture.Root, "linked"), outside);
            WorkspaceSearchResult result = await fixture.Files.SearchAsync("Needle", default);
            Assert.Empty(result.Matches);
            Assert.True(result.Partial); // The binary .cs file cannot be read as text.
        }
        finally { Directory.Delete(outside, true); }
    }

    [Fact]
    public async Task TruncatedContentIsReportedAndSearchCancellationIsObserved()
    {
        using Fixture fixture = new();
        File.WriteAllText(Path.Combine(fixture.Root, "large.cs"), new string('x', 300) + "BeyondPreview");
        LocalWorkspaceFiles limited = new(fixture.Root, new() { MaxPreviewCharacters = 256 });
        WorkspaceSearchResult result = await limited.SearchAsync("BeyondPreview", default);
        Assert.True(result.Partial); Assert.Empty(result.Matches);
        using CancellationTokenSource cancel = new(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => limited.SearchAsync("large", cancel.Token));
    }

    [Fact]
    public async Task SupersededSearchIsSerializedAndCannotPublishOldResults()
    {
        using Fixture fixture = new(); DelayedSearchFiles files = new();
        using GraphicalWorkspaceController ui = new(new(fixture.Root), files, new(new()), Path.Combine(fixture.Root, "keys.json"), fixture.AppearancePath);
        ui.FocusSearch(); ui.Text("old");
        await files.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        ui.Key(InputKey.Parse("Ctrl+A"), 0); ui.Text("new");
        Assert.Null(ui.SearchResult); Assert.True(ui.Searching);
        files.First.SetResult(new([], 1, false, "old"));
        await Wait(ui, () => !ui.Searching);
        Assert.Equal("new", ui.SearchResult!.Summary);
        Assert.Equal(1, files.MaximumConcurrency);
        ui.FocusSearch(); ui.ClearSearch(); ui.Tick(0); Assert.Null(ui.SearchResult);
    }

    [Theory]
    [InlineData("#000000")]
    [InlineData("#FFFFFF")]
    [InlineData("#C52E71")]
    public void PickerPreservesArbitraryRgbColors(string hex)
    {
        RgbColor color = RgbColor.Parse(hex); var hsv = color.Hsv;
        Assert.Equal(color, RgbColor.FromHsv(hsv.Hue, hsv.Saturation, hsv.Value));
    }
    private static async Task Wait(GraphicalWorkspaceController ui, Func<bool> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        while (true) { ui.Tick(0); if (condition()) return; await Task.Delay(5, timeout.Token); }
    }
    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "oryn-appearance-search-" + Guid.NewGuid());
        public string AppearancePath => Path.Combine(Root, "appearance.json");
        public LocalWorkspaceFiles Files { get; }
        public Fixture() { Directory.CreateDirectory(Root); Files = new(Root, new()); }
        public GraphicalWorkspaceController Create() => new(new(Root), Files, new(new()), Path.Combine(Root, "keys.json"), AppearancePath);
        public void Dispose() => Directory.Delete(Root, true);
    }
    private sealed class DelayedSearchFiles : IWorkspaceFiles
    {
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<WorkspaceSearchResult> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _concurrent;
        public int MaximumConcurrency { get; private set; }
        public Task<DirectoryResult> ListAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public Task<FilePreview> ReadAsync(string path, CancellationToken token) => throw new NotSupportedException();
        public async Task<WorkspaceSearchResult> SearchAsync(string query, CancellationToken token)
        {
            MaximumConcurrency = Math.Max(MaximumConcurrency, ++_concurrent);
            try
            {
                if (query == "old") { FirstStarted.SetResult(); return await First.Task; }
                return new([], 1, false, query);
            }
            finally { _concurrent--; }
        }
    }
}
