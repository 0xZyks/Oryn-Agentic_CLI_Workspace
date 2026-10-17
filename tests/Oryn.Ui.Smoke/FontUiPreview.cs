using Oryn.App;
using Oryn.Ui.Sdl;
using SkiaSharp;

/// <summary>Render-only local font specimen; no SDL window, test suite or model calls.</summary>
internal static class FontUiPreview
{
    internal const string Symbols = "\uf07b \uf120 \uf121 \uf013 \uf00c \uf071 \U000f02dc";
    internal const string Unicode = "✓ ✗ ⚠ → ↔ ∑ ∈ ≤ ∞ 世界 👋 e\u0301";

    public static int Run(string[] args)
    {
        string output = Path.GetFullPath(args.Length > 1 ? args[1] : "artifacts/m1/font-ui-preview");
        Directory.CreateDirectory(output);
        string root = Path.Combine(Path.GetTempPath(), "oryn-font-preview-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            using GraphicalWorkspaceController ui = new(new(root), new LocalWorkspaceFiles(root, new()), new(new()), Path.Combine(root, "keys.json"));
            ui.Execute(KeyboardAction.FocusChat, 0);
            string source = $"""
                # Symboles et icônes

                Nerd Font : {Symbols}

                Unicode : {Unicode}

                **Gras : {Symbols}**

                *Italique : {Symbols}*

                `Code inline : {Symbols}`

                ```text
                Dossier {"\uf07b"}   Terminal {"\uf120"}   Code {"\uf121"}   Maison {"\U000f02dc"}
                Unicode : {Unicode}
                ```

                Les mêmes glyphes restent sélectionnables après redimensionnement.
                """;
            var message = new ChatPreviewMessage(ChatPreviewRole.Oryn, source);
            ui.State.Chat.Load([message]); ui.Text("Icônes dans la saisie : " + Symbols);
            using WorkspacePainter painter = new(ui);
            Save(painter, output, "fonts.png", 1280, 1150);
            ui.BeginChatSelection(message.Id, source.IndexOf("Nerd Font", StringComparison.Ordinal));
            ui.ExtendChatSelection(message.Id, source.IndexOf("\n\nUnicode", StringComparison.Ordinal)); ui.PointerUp();
            Save(painter, output, "fonts-selection.png", 1280, 1150);
            Save(painter, output, "fonts-hidpi.png", 1280, 1150, 2);
            Save(painter, output, "fonts-compact.png", 630, 950);
            using WorkspaceFonts fonts = new();
            using SKTypeface primary = SKTypeface.FromFamilyName("JetBrains Mono");
            using SKFont font = new(primary, 15);
            List<string> coverage = ["Primary: " + primary.FamilyName];
            foreach (var run in fonts.Runs(Symbols + " " + Unicode, font))
                coverage.Add($"{run.Font.Typeface.FamilyName}: {run.Text} | " +
                    string.Join(" ", run.Text.EnumerateRunes().Select(r => $"U+{r.Value:X}={run.Font.ContainsGlyph(r.Value)}")));
            File.WriteAllLines(Path.Combine(output, "glyph-coverage.txt"), coverage);
            File.WriteAllText(Path.Combine(output, "source.md"), source);
            Console.WriteLine("Render-only font specimens: " + output);
            return 0;
        }
        finally { Directory.Delete(root, true); }
    }

    private static void Save(WorkspacePainter painter, string output, string name, int width, int height, int scale = 1)
    {
        using SKBitmap bitmap = new(new SKImageInfo(width * scale, height * scale)); using SKCanvas canvas = new(bitmap);
        painter.Draw(canvas, width, height, scale, scale, animationTime: 0);
        using SKImage image = SKImage.FromBitmap(bitmap); using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var file = File.Create(Path.Combine(output, name)); data.SaveTo(file);
    }
}
