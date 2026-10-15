using Oryn.App;
using Oryn.Ui.Sdl;
using SkiaSharp;

internal static class AppearanceSearchChecks
{
    public static void Run(string root, string captures)
    {
        string fixture = Path.Combine(root, "appearance-fixture"); Directory.CreateDirectory(fixture);
        try
        {
            foreach (string name in new[] { "Engine.cs", "model.rs", "worker.py", "entry.ts", "settings.json", "notes.md", "image.svg", "launch.sh", "unknown.zzz" })
                File.WriteAllText(Path.Combine(fixture, name), "// fixture\nvoid FindEngine() {}\n");
            LocalWorkspaceFiles files = new(fixture, new()); WorkspaceState state = new(fixture);
            state.LoadRoot(files.ListAsync(fixture, default).GetAwaiter().GetResult()); state.Dispatch(WorkspaceCommand.ChatTab);
            using GraphicalWorkspaceController ui = new(state, files, new(new()), Path.Combine(fixture, "keys.json"), Path.Combine(fixture, "appearance.json"));
            using WorkspacePainter painter = new(ui);
            using SKBitmap bitmap = new(new SKImageInfo(1280, 836)); using SKCanvas canvas = new(bitmap);
            painter.Draw(canvas, 1280, 836, animationTime: 0); Save(bitmap, Path.Combine(captures, "21-file-icons.png"));
            PanelBounds explorer = ui.Layout.Explorer;
            ui.FocusPanel(WorkspacePanel.Explorer);
            painter.PointerDown((ui.Layout.Center.X + 5) * 9, ui.Layout.Center.Y * 22 + ui.Spacing.PanelTopOffset - 4);
            if (state.Focus != WorkspacePanel.Explorer) throw new InvalidOperationException("Navbar/panel gap must not receive panel clicks.");
            if (bitmap.GetPixel(ui.Layout.Center.X * 9 + 5, ui.Layout.Center.Y * 22 + ui.Spacing.PanelTopOffset - 4) != SKColor.Parse(ui.Appearance.ThemeColor))
                throw new InvalidOperationException("Panel top gap must reveal the theme background.");
            painter.PointerDown((explorer.X + 7) * 9, (explorer.Y + ui.Spacing.ExplorerHeaderRows - 1) * 22 + ui.Spacing.Gap + 10); painter.PointerUp();
            if (!ui.SearchFocused || ui.InputScope != KeyboardScope.SearchEditor) throw new InvalidOperationException("Search click must focus its text editor.");
            ui.Text("FindEngine");
            Wait(ui, () => !ui.Searching);
            painter.Draw(canvas, 1280, 836, animationTime: 0); Save(bitmap, Path.Combine(captures, "22-search-results.png"));
            if (ui.SearchResult?.Matches.Count != 9) throw new InvalidOperationException("Text search must find matching files with source lines.");
            ui.Editor.Set("chat preserved"); state.Chat.Draft = "chat preserved";
            ui.SelectTab(WorkspaceTab.Chat); ui.Key(InputKey.Parse("Enter"), 0); ui.Text("!");
            if (ui.SearchEditor.Text != "FindEngine" || state.Chat.Draft != "chat preserved!") throw new InvalidOperationException("Tab click must transfer text focus from search to chat.");
            ui.OpenAppearance(); painter.Draw(canvas, 1280, 836, animationTime: 0);
            PanelBounds box = new((ui.Columns - 72) / 2, (ui.Rows - 24) / 2, 72, 24);
            float hueX = (box.X + 2 + (box.Width - 4) * .45f) * 9, hueY = (box.Y + box.Height - 9) * 22 + 11;
            painter.PointerDown(hueX, hueY); painter.Hover(hueX + 30, hueY); painter.PointerUp();
            if (ui.DisplayAppearance.AccentColor == "#B49AF2") throw new InvalidOperationException("Hue picker must respond to mouse dragging.");
            painter.Draw(canvas, 1280, 836, animationTime: 0); Save(bitmap, Path.Combine(captures, "23-color-picker.png"));
            ui.SelectAppearanceTarget(1); ui.AppearanceEditor.SelectAll(); ui.Text("#ECEEF3");
            ui.ApplyAppearance(false); painter.Draw(canvas, 1280, 836, animationTime: 0); Save(bitmap, Path.Combine(captures, "24-light-theme.png"));
            if (bitmap.GetPixel(1, bitmap.Height - 1) != SKColor.Parse("#ECEEF3")) throw new InvalidOperationException("Theme color must affect the frame.");
            ui.Execute(KeyboardAction.FocusVisualizer, 0); painter.Draw(canvas, 1280, 836, animationTime: 0); Save(bitmap, Path.Combine(captures, "25-light-graph.png"));
            ui.OpenAppearance();
            using SKBitmap compact = new(new SKImageInfo(360, 264)); using SKCanvas small = new(compact);
            painter.Draw(small, 360, 264, animationTime: 0); Save(compact, Path.Combine(captures, "26-appearance-compact.png"));
            ui.Key(InputKey.Parse("Tab"), 0); ui.Key(InputKey.Parse("Ctrl+A"), 0); ui.Text("#203846"); ui.Key(InputKey.Parse("Alt+S"), 0);
            if (ui.Overlay != WorkspaceOverlay.None || !File.Exists(Path.Combine(fixture, "appearance.json")))
                throw new InvalidOperationException("Compact settings must remain keyboard accessible and save.");
            ui.FocusSearch(); painter.Draw(small, 360, 264, animationTime: 0); Save(compact, Path.Combine(captures, "27-search-compact.png"));
            Console.WriteLine("PASS: extension vector icons, search pointer focus, tab focus transfer, picker dragging, light theme/graph and compact keyboard settings.");
        }
        finally { Directory.Delete(fixture, true); }
    }
    private static void Wait(GraphicalWorkspaceController ui, Func<bool> condition)
    {
        long deadline = Environment.TickCount64 + 5000;
        do { ui.Tick(0); if (condition()) return; Thread.Sleep(5); } while (Environment.TickCount64 < deadline);
        throw new TimeoutException("Search did not complete.");
    }
    private static void Save(SKBitmap bitmap, string path)
    {
        using SKImage image = SKImage.FromBitmap(bitmap); using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(path); png.SaveTo(stream);
    }
}
