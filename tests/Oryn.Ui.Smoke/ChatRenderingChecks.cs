using Oryn.App;
using Oryn.Ui.Sdl;
using SkiaSharp;

internal static class ChatRenderingChecks
{
    public static void Run(string root, string captures)
    {
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
