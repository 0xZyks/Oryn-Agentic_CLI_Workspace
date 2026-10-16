using Oryn.App;
using Oryn.Ui.Sdl;
using SkiaSharp;

internal static class ChatRenderingChecks
{
    public static void Run(string root, string captures)
    {
        using (GraphicalWorkspaceController ui = new(new(root), new LocalWorkspaceFiles(root, new()), new(new()), Path.Combine(root, "unused-panel-keys.json")))
        using (WorkspacePainter painter = new(ui))
        using (SKBitmap bitmap = new(new SKImageInfo(1280, 836)))
        using (SKCanvas canvas = new(bitmap))
        {
            ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("abcdef");
            painter.Draw(canvas, 1280, 836, animationTime: 0);
            PanelBounds pane = ui.Layout.Center;
            // The top padding is panel chrome, not a tab hit region.
            painter.PointerDown((pane.X + 9) * 9, pane.Y * 22 + ui.Spacing.PanelTopOffset + 3);
            if (ui.State.ActiveTab != WorkspaceTab.Chat || ui.State.FocusArea != PanelFocusArea.Content)
                throw new InvalidOperationException("Central-tab top padding must not select another tab.");
            // The lower part of the inset tab still selects the same tab as its label.
            painter.PointerDown((pane.X + 9) * 9, pane.Y * 22 + ui.Spacing.PanelTopOffset + 26);
            if (ui.State.ActiveTab != WorkspaceTab.Code || ui.State.FocusArea != PanelFocusArea.Tabs)
                throw new InvalidOperationException("Extended tab header must select Sources and own keyboard tab focus.");
            ui.Key(InputKey.Parse("Right"), 0);
            if (ui.State.ActiveTab != WorkspaceTab.Visualizer)
                throw new InvalidOperationException("Keyboard order must match Chat / Sources / Visualiser.");
            ui.Execute(KeyboardAction.FocusChat, 0); painter.Draw(canvas, 1280, 836, animationTime: 0);
            painter.PointerDown((pane.X + 6) * 9 + 1, (pane.Y + pane.Height - 2) * 22 - 10 - ui.Spacing.Gap + 5);
            if (ui.Editor.Cursor != 2) throw new InvalidOperationException("Raised composer must preserve click-to-caret alignment.");
            painter.PointerDown((pane.X + pane.Width - 2) * 9, (pane.Y + pane.Height) * 22 - 5);
            if (ui.Editor.Cursor != 2) throw new InvalidOperationException("Bottom panel margin must not receive composer clicks.");
            Save(bitmap, Path.Combine(captures, "21-panel-focus-chat.png"));
            ui.Execute(KeyboardAction.FocusFiles, 0); painter.Draw(canvas, 1280, 836, animationTime: 0);
            Save(bitmap, Path.Combine(captures, "22-panel-focus-sources.png"));
        }
        Console.WriteLine("PASS: taller tab hit regions match keyboard order; raised composer clicks and bottom margin stay aligned.");
        using (GraphicalWorkspaceController ui = new(new(root), new LocalWorkspaceFiles(root, new()), new(new()), Path.Combine(root, "unused-copy-keys.json")))
        using (WorkspacePainter painter = new(ui))
        using (SKBitmap bitmap = new(new SKImageInfo(1280, 836)))
        using (SKCanvas canvas = new(bitmap))
        {
            ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("unsent draft");
            var message = new ChatPreviewMessage(ChatPreviewRole.Oryn, "Code :\n世界 👋");
            ui.State.Chat.Load([message]); ui.State.Chat.FailReply("llama-server failure");
            painter.Draw(canvas, 1280, 836, animationTime: 0);
            PanelBounds pane = ui.Layout.Center;
            int firstRow = pane.Y + ui.Spacing.ContentTopRows + 2;
            painter.Click(pane.X + 3, firstRow + 3);
            if (ui.TakeClipboardText() != message.Text || ui.Editor.Text != "unsent draft")
                throw new InvalidOperationException("Copy button must copy the complete Unicode message without editing the draft.");
            painter.Click(pane.X + 3, firstRow + 6);
            if (ui.TakeClipboardText() != ui.State.Chat.Error)
                throw new InvalidOperationException("Error copy button must preserve the backend diagnostic.");
            Save(bitmap, Path.Combine(captures, "23-chat-copy.png"));
        }
        Console.WriteLine("PASS: chat message and error copy hit regions preserve full text and the draft.");
        using (GraphicalWorkspaceController ui = new(new(root), new LocalWorkspaceFiles(root, new()), new(new()), Path.Combine(root, "unused-selection-keys.json")))
        using (WorkspacePainter painter = new(ui))
        using (SKBitmap bitmap = new(new SKImageInfo(1280, 836)))
        using (SKCanvas canvas = new(bitmap))
        {
            ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("draft");
            var message = new ChatPreviewMessage(ChatPreviewRole.User, new string('x', 100));
            ui.State.Chat.Load([message]); painter.Draw(canvas, 1280, 836, animationTime: 0);
            PanelBounds pane = ui.Layout.Center; int firstRow = pane.Y + ui.Spacing.ContentTopRows + 2;
            float left = (pane.X + 2) * 9, bodyY = (firstRow + 2) * 22 + 10;
            // Columns beyond the previous 76-character cap must still be on the first body row.
            painter.PointerDown(left + 80 * 9, bodyY); painter.Hover(left + 90 * 9, bodyY); painter.PointerUp();
            string selected = ui.State.Chat.Selection.Text(ui.State.Chat.Messages);
            if (selected.Length < 8 || selected.Any(c => c != 'x'))
                throw new InvalidOperationException("History selection must use the full chat width beyond column 76.");
            ui.Key(InputKey.Parse("Ctrl+C"), 0);
            if (ui.TakeClipboardText() != selected || ui.State.Chat.Draft != "draft")
                throw new InvalidOperationException("Mouse selection and Ctrl+C must preserve the original text and draft.");
            using SKBitmap small = new(new SKImageInfo(630, 484)); using SKCanvas smallCanvas = new(small);
            painter.Draw(smallCanvas, 630, 484, animationTime: 0);
            if (ui.State.Chat.Selection.Text(ui.State.Chat.Messages) != selected)
                throw new InvalidOperationException("Resizing/reflow must not change the selected substring.");
            Save(small, Path.Combine(captures, "25-chat-selection-compact.png"));
        }
        Console.WriteLine("PASS: full-width history supports mouse selection and Ctrl+C; reflow preserves original offsets and draft.");
        using (GraphicalWorkspaceController ui = new(new(root), new LocalWorkspaceFiles(root, new()), new(new()), Path.Combine(root, "unused-markdown-keys.json")))
        using (WorkspacePainter painter = new(ui))
        using (SKBitmap bitmap = new(new SKImageInfo(900, 660)))
        using (SKCanvas canvas = new(bitmap))
        {
            const string source = "Voici **du gras** et `du code`.";
            ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("draft");
            var message = new ChatPreviewMessage(ChatPreviewRole.Oryn, source); ui.State.Chat.Load([message]);
            painter.Draw(canvas, 900, 660, animationTime: 0);
            PanelBounds pane = ui.Layout.Center; int bodyRow = pane.Y + ui.Spacing.ContentTopRows + 3;
            painter.PointerDown((pane.X + 2) * 9, bodyRow * 22 + 10);
            painter.Hover((pane.X + pane.Width - 2) * 9, bodyRow * 22 + 10); painter.PointerUp();
            ui.Key(InputKey.Parse("Ctrl+C"), 0);
            if (ui.TakeClipboardText() != "Voici du gras et du code." || ui.State.Chat.Draft != "draft")
                throw new InvalidOperationException("Markdown hit testing must copy the visible excerpt without hidden emphasis/code markers.");
            ui.CopyChatMessage(message.Id);
            if (ui.TakeClipboardText() != source) throw new InvalidOperationException("Full-message copy must preserve raw Markdown.");
        }
        Console.WriteLine("PASS: styled Markdown selection maps to original source spans; excerpt and full-message copying remain distinct.");
        using (GraphicalWorkspaceController ui = new(new(root), new LocalWorkspaceFiles(root, new()), new(new()), Path.Combine(root, "unused-drag-scroll-keys.json")))
        using (WorkspacePainter painter = new(ui))
        using (SKBitmap bitmap = new(new SKImageInfo(900, 660)))
        using (SKCanvas canvas = new(bitmap))
        {
            ui.Execute(KeyboardAction.FocusChat, 0);
            ui.State.Chat.Load([new(ChatPreviewRole.Oryn, string.Join("\n", Enumerable.Range(0, 60).Select(i => $"original line {i}")))]);
            painter.Draw(canvas, 900, 660, animationTime: 0);
            PanelBounds pane = ui.Layout.Center; int firstRow = pane.Y + ui.Spacing.ContentTopRows + 2;
            float x = (pane.X + 4) * 9, y = (firstRow + 2) * 22 + 10;
            painter.PointerDown(x, y); painter.Hover(x, firstRow * 22 - 3);
            painter.Draw(canvas, 900, 660, animationTime: 200);
            if (ui.ChatScroll == 0 || !ui.State.Chat.Selection.Dragging)
                throw new InvalidOperationException("Dragging above history must scroll while keeping the selection active.");
            painter.PointerUp(); ui.Key(InputKey.Parse("Ctrl+C"), 0);
            if (string.IsNullOrEmpty(ui.TakeClipboardText()))
                throw new InvalidOperationException("Scrolled selection must retain copyable original text.");
            painter.PointerDown(x, y, right: true);
            if (ui.State.Chat.Selection.Dragging)
                throw new InvalidOperationException("Right-click must not leave a left-button text drag active.");
            ui.State.Chat.Load([new(ChatPreviewRole.Oryn, "short background")]); ui.OpenOverlay(WorkspaceOverlay.ElementChat);
            ui.CurrentChat.Load([new(ChatPreviewRole.Oryn, string.Join("\n", Enumerable.Range(0, 60).Select(i => $"element line {i}")))]);
            ui.SetChatScroll(1000, 1000); painter.Draw(canvas, 900, 660, animationTime: 300);
            if (ui.ChatScroll == 0)
                throw new InvalidOperationException("Background chat rendering must not reset the element discussion's scroll position.");
        }
        Console.WriteLine("PASS: dragging outside history scrolls and remains copyable; right-click never starts a text drag.");
        {
            string config = Path.Combine(root, "unused-setup-settings.json");
            var settings = new WorkspaceSettings { InferenceAssetsRoot = Path.Combine(root, "missing-setup-assets"),
                ModelProfiles = new() { ["core"] = new() { CatalogId = "gptoss20b-mxfp4", CpuMoeLayers = 4, FlashAttention = "on" } } };
            using GraphicalWorkspaceController ui = new(new(root), new LocalWorkspaceFiles(root, settings), new(new()),
                Path.Combine(root, "unused-setup-keys.json"), settings: settings, configPath: config,
                inferenceProvider: new Oryn.TestSupport.StubInferenceProvider());
            using WorkspacePainter painter = new(ui);
            using SKBitmap bitmap = new(new SKImageInfo(1280, 836)); using SKCanvas canvas = new(bitmap);
            var hardware = new HardwareSnapshot("linux-x64", 12, 16L << 30, 10L << 30, 50L << 30,
                [new("RTX 3060", 12L << 30, 11L << 30)], "fixture");
            var suggestions = HardwareProbe.Suggest(hardware);
            ui.InferenceSetup.Refresh(hardware, suggestions, "gptoss20b-mxfp4");
            ui.OpenOverlay(WorkspaceOverlay.InferenceSetup);
            painter.Draw(canvas, 1280, 836, animationTime: 0);
            int x = (ui.Columns - 100) / 2, y = (ui.Rows - 30) / 2;
            int fast = suggestions.ToList().FindIndex(s => s.Model.Id == "8b-q4");
            painter.Click(x + 3, y + 7 + fast);
            if (ui.InferenceSetup.Selected?.Model.Id != "8b-q4" || ui.InferenceSetup.ConfirmDownload)
                throw new InvalidOperationException("Selecting a setup row must highlight that candidate without applying/downloading.");
            painter.Draw(canvas, 1280, 836, animationTime: 0);
            painter.Click(x + 3, y + 26);
            if (!ui.InferenceSetup.ConfirmDownload || File.Exists(config))
                throw new InvalidOperationException("Setup Apply must request consent for missing assets before saving or downloading.");
            painter.Draw(canvas, 1280, 836, animationTime: 0);
            Save(bitmap, Path.Combine(captures, "24-setup-selection.png"));
        }
        Console.WriteLine("PASS: setup row selection and Apply hit regions remain distinct; missing assets require confirmation.");
        using (GraphicalWorkspaceController ui = new(new(root), new LocalWorkspaceFiles(root, new()), new(new()), Path.Combine(root, "unused-workshop-keys.json")))
        using (WorkspacePainter painter = new(ui))
        using (SKBitmap bitmap = new(new SKImageInfo(1280, 836)))
        using (SKCanvas canvas = new(bitmap))
        {
            ui.State.Dispatch(WorkspaceCommand.VisualizerTab); ui.State.Graph.ChangeZoom(-1);
            painter.Draw(canvas, 1280, 836, animationTime: 0);
            Save(bitmap, Path.Combine(captures, "19-workshop-overview.png"));
            PanelBounds pane = ui.Layout.Center;
            painter.Click(pane.X + pane.Width - 20, pane.Y + pane.Height - 4);
            if (ui.Overlay != WorkspaceOverlay.ElementChat || ui.OverlayTarget?.Id != ui.State.Graph.SelectedId)
                throw new InvalidOperationException("Inspector discussion must open the selected UUID chat.");
            ui.CloseOverlay(); painter.Draw(canvas, 1280, 836, animationTime: 0);
            painter.Click(pane.X + pane.Width - 20, pane.Y + pane.Height - 6);
            if (ui.Overlay != WorkspaceOverlay.Details || ui.OverlayTarget?.Id != ui.State.Graph.SelectedId)
                throw new InvalidOperationException("Inspector details must follow the selected UUID.");
            ui.CloseOverlay(); painter.Draw(canvas, 1280, 836, animationTime: 0);
            painter.PointerDown(400, 632); // Bottom-row card in the centered overview.
            if (ui.State.Graph.Placement.DraggedId != ui.State.Graph.Nodes.Last().Id)
                throw new InvalidOperationException("Centered card hit regions must follow the rendered position.");
            ui.PointerMove(400f, 652f); ui.Tick(1000);
            painter.Draw(canvas, 1280, 836, animationTime: 0);
            using SKBitmap beforeRelease = bitmap.Copy();
            ui.PointerUp(); painter.Draw(canvas, 1280, 836, animationTime: 0);
            for (int y = 0; y < bitmap.Height; y++)
            for (int x = 0; x < bitmap.Width; x++)
                if (beforeRelease.GetPixel(x, y) != bitmap.GetPixel(x, y))
                    throw new InvalidOperationException("Releasing a card inside the viewport must not recenter the scene.");
            using SKBitmap compact = new(new SKImageInfo(540, 396)); using SKCanvas small = new(compact);
            painter.Draw(small, 540, 396, animationTime: 0); Save(compact, Path.Combine(captures, "20-workshop-compact.png"));
            if (!ui.Layout.Compact || !ui.GraphActive) throw new InvalidOperationException("Compact workshop must retain graph focus.");
        }
        Console.WriteLine("PASS: workshop inspector opens details and chat for the selected UUID; compact architecture retains keyboard focus; centered dragging does not jump on release.");
        foreach (bool unicode in new[] { true, false })
        {
            using GraphicalWorkspaceController ui = new(new(root), new LocalWorkspaceFiles(root, new()), new(new()), Path.Combine(root, "unused-render-keys.json"), inferenceProvider: new Oryn.TestSupport.StubInferenceProvider());
            ui.Execute(KeyboardAction.FocusChat, 0); ui.Text("Coucou"); ui.Key(InputKey.Parse("Enter"), 0);
            using WorkspacePainter painter = new(ui, unicode: unicode);
            using SKBitmap first = new(new SKImageInfo(900, 660)); using SKCanvas firstCanvas = new(first);
            using SKBitmap second = new(new SKImageInfo(900, 660)); using SKCanvas secondCanvas = new(second);
            painter.Draw(firstCanvas, 900, 660, animationTime: 0);
            painter.Draw(secondCanvas, 900, 660, animationTime: 200);
            int changed = 0;
            for (int y = 0; y < first.Height; y++)
            for (int x = 0; x < first.Width; x++)
                if (first.GetPixel(x, y) != second.GetPixel(x, y)) changed++;
            if (changed < 4 || changed > 200) throw new InvalidOperationException($"Spinner must animate within its icon, not alter the conversation: {changed} pixels.");
            if (ui.State.Chat.Messages.Count != 2 || ui.State.Chat.Messages[0].Text != "Coucou" || !ui.State.Chat.IsThinking)
                throw new InvalidOperationException("Rendering altered conversation state.");
            if (unicode)
            {
                Save(first, Path.Combine(captures, "16-thinking-frame-top.png"));
                Save(second, Path.Combine(captures, "17-thinking-frame-right.png"));
            }
        }
        Console.WriteLine("PASS: font-independent chat spinner changes pixels in Unicode and ASCII modes without changing conversation state.");
    }
    private static void Save(SKBitmap bitmap, string path)
    {
        using SKImage image = SKImage.FromBitmap(bitmap); using SKData png = image.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream stream = File.Create(path); png.SaveTo(stream);
    }
}
