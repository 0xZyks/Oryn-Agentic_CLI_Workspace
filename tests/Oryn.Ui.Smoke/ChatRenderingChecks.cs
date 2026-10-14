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
            // The final eight header pixels must select the same tab as its label.
            painter.PointerDown((pane.X + 9) * 9, pane.Y * 22 + 26);
            if (ui.State.ActiveTab != WorkspaceTab.Code || ui.State.FocusArea != PanelFocusArea.Tabs)
                throw new InvalidOperationException("Extended tab header must select Sources and own keyboard tab focus.");
            ui.Key(InputKey.Parse("Right"), 0);
            if (ui.State.ActiveTab != WorkspaceTab.Visualizer)
                throw new InvalidOperationException("Keyboard order must match Chat / Sources / Visualiser.");
            ui.Execute(KeyboardAction.FocusChat, 0); painter.Draw(canvas, 1280, 836, animationTime: 0);
            painter.PointerDown((pane.X + 6) * 9 + 1, (pane.Y + pane.Height - 2) * 22 - 10 + 5);
            if (ui.Editor.Cursor != 2) throw new InvalidOperationException("Raised composer must preserve click-to-caret alignment.");
            painter.PointerDown((pane.X + pane.Width - 2) * 9, (pane.Y + pane.Height) * 22 - 5);
            if (ui.Editor.Cursor != 2) throw new InvalidOperationException("Bottom panel margin must not receive composer clicks.");
            Save(bitmap, Path.Combine(captures, "21-panel-focus-chat.png"));
            ui.Execute(KeyboardAction.FocusFiles, 0); painter.Draw(canvas, 1280, 836, animationTime: 0);
            Save(bitmap, Path.Combine(captures, "22-panel-focus-sources.png"));
        }
        Console.WriteLine("PASS: taller tab hit regions match keyboard order; raised composer clicks and bottom margin stay aligned.");
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
            painter.PointerDown(400, 610); // Bottom-row card in the centered overview.
            if (ui.State.Graph.Placement.DraggedId != ui.State.Graph.Nodes.Last().Id)
                throw new InvalidOperationException("Centered card hit regions must follow the rendered position.");
            ui.PointerMove(400f, 630f); ui.Tick(1000);
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
            using GraphicalWorkspaceController ui = new(new(root), new LocalWorkspaceFiles(root, new()), new(new()), Path.Combine(root, "unused-render-keys.json"));
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
            if (ui.State.Chat.Messages.Count != 1 || ui.State.Chat.Messages[0].Text != "Coucou" || !ui.State.Chat.IsThinking)
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
