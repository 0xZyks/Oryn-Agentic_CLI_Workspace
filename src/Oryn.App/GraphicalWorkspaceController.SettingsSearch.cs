using System.Text.Json;

namespace Oryn.App;

public sealed partial class GraphicalWorkspaceController
{
    public AppearanceSettings Appearance { get; private set; } = new();
    public AppearanceSettings? AppearanceDraft { get; private set; }
    public AppearanceSettings DisplayAppearance => Overlay == WorkspaceOverlay.Appearance && AppearanceDraft is not null ? AppearanceDraft : Appearance;
    public TextEditState AppearanceEditor { get; } = new();
    public int AppearanceTarget { get; private set; }
    public string AppearanceError { get; private set; } = "";
    public float PickerHue { get; private set; }
    public float PickerSaturation { get; private set; }
    public float PickerValue { get; private set; }
    private string _appearancePath = "";
    public TextEditState SearchEditor { get; } = new();
    public bool SearchFocused { get; private set; }
    public bool SearchActive => SearchEditor.Text.Trim().Length > 0;
    public bool Searching { get; private set; }
    public WorkspaceSearchResult? SearchResult { get; private set; }
    public int? CodeMatchLine { get; private set; }
    public int SearchIndex { get; private set; }
    public int SearchTop { get; private set; }
    private string _searchQuery = "";
    private CancellationTokenSource? _search;
    private Task? _searchTask;
    private readonly SemaphoreSlim _searchGate = new(1, 1);
    public TextEditState ActiveEditor => Overlay == WorkspaceOverlay.Keybindings ? BindingEditor
        : Overlay == WorkspaceOverlay.Appearance ? AppearanceEditor
        : SearchFocused && Overlay == WorkspaceOverlay.None ? SearchEditor : Editor;

    private void LoadAppearance(string? path)
    {
        _appearancePath = path ?? AppearanceStore.DefaultPath;
        try { Appearance = AppearanceStore.Load(_appearancePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        { State.SetStatus("Appearance profile unavailable / " + ex.Message); }
    }
    public void OpenAppearance()
    {
        AppearanceDraft = Appearance; AppearanceTarget = 0; AppearanceError = "";
        OpenOverlay(WorkspaceOverlay.Appearance); SyncPicker();
    }
    private void SyncPicker()
    {
        string hex = AppearanceTarget == 0 ? AppearanceDraft!.AccentColor : AppearanceDraft!.ThemeColor;
        AppearanceEditor.Set(hex); (PickerHue, PickerSaturation, PickerValue) = RgbColor.Parse(hex).Hsv;
    }
    public void SelectAppearanceTarget(int target)
    {
        AppearanceTarget = Math.Clamp(target, 0, 1); AppearanceError = ""; SyncPicker(); Dirty = true;
    }
    public void SetPicker(float? hue = null, float? saturation = null, float? value = null)
    {
        if (AppearanceDraft is null) return;
        PickerHue = Math.Clamp(hue ?? PickerHue, 0, 359.999f);
        PickerSaturation = Math.Clamp(saturation ?? PickerSaturation, 0, 1);
        PickerValue = Math.Clamp(value ?? PickerValue, 0, 1);
        SetAppearanceColor(RgbColor.FromHsv(PickerHue, PickerSaturation, PickerValue).Hex);
        AppearanceEditor.Set(AppearanceTarget == 0 ? AppearanceDraft.AccentColor : AppearanceDraft.ThemeColor);
        AppearanceError = ""; Dirty = true;
    }
    private void SetAppearanceColor(string hex)
    {
        AppearanceDraft = AppearanceTarget == 0 ? AppearanceDraft! with { AccentColor = hex } : AppearanceDraft! with { ThemeColor = hex };
    }
    private bool UpdateAppearanceHex()
    {
        try
        {
            RgbColor color = RgbColor.Parse(AppearanceEditor.Text.Trim());
            SetAppearanceColor(color.Hex); (PickerHue, PickerSaturation, PickerValue) = color.Hsv;
            AppearanceError = ""; return true;
        }
        catch (ArgumentException ex) { AppearanceError = ex.Message; return false; }
    }
    public bool ApplyAppearance(bool save)
    {
        if (!UpdateAppearanceHex()) return false;
        try
        {
            AppearanceSettings next = AppearanceDraft!.Validated();
            if (save) AppearanceStore.Save(_appearancePath, next);
            Appearance = next; CloseOverlay(); State.SetStatus(save ? "Appearance saved" : "Appearance applied for this session"); return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { AppearanceError = ex.Message; Dirty = true; return false; }
    }
    public void ResetAppearance() { AppearanceDraft = new(); SyncPicker(); AppearanceError = ""; Dirty = true; }
    public void FocusSearch()
    {
        FocusPanel(WorkspacePanel.Explorer); SearchFocused = true; Dirty = true;
    }
    public void ClearSearch()
    {
        _search?.Cancel(); _searchQuery = ""; SearchEditor.Set(""); SearchResult = null; SearchIndex = SearchTop = 0; Searching = false;
        SearchFocused = false; Composition = ""; Dirty = true;
    }
    public void EditorChanged()
    {
        if (Overlay == WorkspaceOverlay.Appearance) UpdateAppearanceHex();
        else if (Overlay == WorkspaceOverlay.Keybindings) { }
        else if (SearchFocused && Overlay == WorkspaceOverlay.None) QueueSearch();
        else CurrentChat.Draft = Editor.Text;
        Dirty = true;
    }
    private void QueueSearch()
    {
        string query = SearchEditor.Text.Trim();
        if (query == _searchQuery) return;
        _searchQuery = query;
        _search?.Cancel(); _search?.Dispose(); _search = new();
        SearchResult = null; SearchIndex = SearchTop = 0;
        Searching = query.Length > 0;
        if (!Searching) return;
        _searchTask = RunSearchAsync(query, _search.Token);
    }
    private async Task RunSearchAsync(string query, CancellationToken token)
    {
        try
        {
            await Task.Delay(200, token);
            await _searchGate.WaitAsync(token);
            WorkspaceSearchResult result;
            try { result = await _files.SearchAsync(query, token); }
            finally { _searchGate.Release(); }
            _completed.Enqueue(() => { if (!token.IsCancellationRequested) { SearchResult = result; Searching = false; KeepSearchVisible(); } });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { _completed.Enqueue(() => { if (!token.IsCancellationRequested) { SearchResult = new([], 0, true, "Search failed / " + ex.Message); Searching = false; } }); }
    }
    public void MoveSearchResult(int delta)
    {
        SearchIndex = Math.Clamp(SearchIndex + delta, 0, Math.Max(0, (SearchResult?.Matches.Count ?? 0) - 1));
        KeepSearchVisible(); Dirty = true;
    }
    private void KeepSearchVisible()
    {
        int visible = Spacing.SearchRows(Layout.Explorer.Height);
        if (SearchIndex < SearchTop) SearchTop = SearchIndex;
        if (SearchIndex >= SearchTop + visible) SearchTop = SearchIndex - visible + 1;
        SearchTop = Math.Clamp(SearchTop, 0, Math.Max(0, (SearchResult?.Matches.Count ?? 0) - visible));
    }
    public void SelectSearchResult(int index)
    {
        FocusPanel(WorkspacePanel.Explorer); SearchIndex = index; KeepSearchVisible(); OpenSearchResult();
    }
    public void OpenSearchResult()
    {
        if (SearchResult is not { } result || SearchIndex < 0 || SearchIndex >= result.Matches.Count) return;
        WorkspaceSearchMatch match = result.Matches[SearchIndex];
        CancelPendingRead(); SearchFocused = false;
        _load = new(); Loading = true; Dirty = true;
        _loadingTask = ReadSearchResultAsync(match, SearchEditor.Text.Trim(), _load.Token);
    }
    private async Task ReadSearchResultAsync(WorkspaceSearchMatch match, string query, CancellationToken token)
    {
        try
        {
            FilePreview preview = await _files.ReadAsync(match.Entry.Path, token);
            _completed.Enqueue(() =>
            {
                if (token.IsCancellationRequested) return;
                State.Apply(new(match.Entry, null, preview));
                CodeMatchLine = null;
                if (match.Line is not null && preview.Error is null)
                {
                    int offset = preview.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                    if (offset >= 0)
                    {
                        int line = 1;
                        for (int i = 0; i < offset; i++) if (preview.Content[i] == '\n') line++;
                        CodeMatchLine = line;
                        State.SetStatus($"Text match / {match.RelativePath}:{line} / read-only");
                    }
                    else State.SetStatus("File changed / search text no longer present / read-only");
                }
                CodeTop = Math.Max(0, (CodeMatchLine ?? 1) - 1); CodeLeft = 0; Loading = false;
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { _completed.Enqueue(() => { if (!token.IsCancellationRequested) { State.SetStatus(ex.Message); Loading = false; } }); }
    }
}
