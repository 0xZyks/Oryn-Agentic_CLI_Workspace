using Oryn.Ui.Sdl;
using SkiaSharp;

internal static class FontFallbackChecks
{
    public static void Run()
    {
        using SKTypeface primary = SKTypeface.FromFamilyName("monospace");
        using SKFont font = new(primary, 19) { Embolden = true, SkewX = -.18f };
        using WorkspaceFonts fonts = new();
        string text = "A" + FontUiPreview.Symbols + "e\u0301";
        var runs = fonts.Runs(text, font).ToArray();
        if (string.Concat(runs.Select(r => r.Text)) != text)
            throw new InvalidOperationException("Font fallback must preserve Unicode scalars, including supplementary Nerd icons.");
        foreach (var run in runs)
        {
            if (!run.Font.ContainsGlyphs(run.Text)) throw new InvalidOperationException("Bundled Nerd icons must resolve to real glyphs without system installation.");
            if (run.Font.Size != font.Size || run.Font.Embolden != font.Embolden || run.Font.SkewX != font.SkewX)
                throw new InvalidOperationException("Fallback must preserve Markdown size and style.");
        }
        if (Math.Abs(fonts.MeasureText(text, font) - runs.Sum(r => r.Font.MeasureText(r.Text))) > .01f)
            throw new InvalidOperationException("Layout and drawing must use the same fallback advances.");
        const string unavailable = "\U0010ffff";
        if (string.Concat(fonts.Runs(unavailable, font).Select(r => r.Text)) != unavailable)
            throw new InvalidOperationException("Unknown glyphs must not silently delete source text.");
        Console.WriteLine("PASS: bundled Nerd icons, supplementary scalars, Markdown style and matching fallback advances.");
    }
}
