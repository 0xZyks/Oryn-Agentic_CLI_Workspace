using System.Globalization;
using System.Text;
using SkiaSharp;

namespace Oryn.Ui.Sdl;

/// <summary>Local glyph fallback shared by drawing and layout; owns its native font resources.</summary>
internal sealed class WorkspaceFonts : IDisposable
{
    private const int MaxCharacters = 2048, MaxFaces = 64;
    private readonly SKTypeface _symbols;
    private readonly Dictionary<int, SKTypeface?> _characters = [];
    private readonly Dictionary<nint, SKTypeface> _faces = [];
    private readonly Dictionary<(SKFont Base, nint Face), SKFont> _fonts = [];
    private SKTypeface[]? _knownFallbacks;

    public WorkspaceFonts()
    {
        using Stream stream = typeof(WorkspaceFonts).Assembly.GetManifestResourceStream("Oryn.Fonts.SymbolsNerdFontMono-Regular.ttf")
            ?? throw new InvalidOperationException("Bundled Nerd Font resource is missing.");
        using SKData data = SKData.Create(stream);
        _symbols = SKTypeface.FromData(data) ?? throw new InvalidOperationException("Bundled Nerd Font could not be loaded.");
    }

    public float MeasureText(string text, SKFont font)
    {
        float width = 0;
        foreach (var run in Runs(text, font)) width += run.Font.MeasureText(run.Text);
        return width;
    }

    public void DrawText(SKCanvas canvas, string text, float x, float baseline, SKFont font, SKPaint paint)
    {
        foreach (var run in Runs(text, font))
        {
            canvas.DrawText(run.Text, x, baseline, SKTextAlign.Left, run.Font, paint);
            x += run.Font.MeasureText(run.Text);
        }
    }

    // Keep a grapheme in one face when possible. Otherwise fall back by Unicode scalar,
    // never by UTF-16 code unit (Nerd Fonts also use supplementary private-use code points).
    internal IEnumerable<(string Text, SKFont Font)> Runs(string text, SKFont font)
    {
        if (text.Length == 0) yield break;
        if (font.ContainsGlyphs(text)) { yield return (text, font); yield break; }
        StringBuilder buffer = new(); SKFont? current = null;
        var elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            string value = elements.GetTextElement();
            // These affect shaping, which this renderer does not yet implement. Do not
            // draw their missing-glyph boxes; original text and selection offsets stay intact.
            if (value.EnumerateRunes().Any(Ignorable))
                value = string.Concat(value.EnumerateRunes().Where(r => !Ignorable(r)).Select(r => r.ToString()));
            if (value.Length == 0) continue;
            SKFont? whole = font.ContainsGlyphs(value) ? font
                : _symbols.ContainsGlyphs(value) ? Styled(font, _symbols) : null;
            if (whole is null)
            {
                foreach (Rune rune in value.EnumerateRunes())
                {
                    SKFont candidate = Resolve(rune.Value, font);
                    if (candidate.ContainsGlyphs(value)) { whole = candidate; break; }
                }
            }
            if (whole is not null)
            {
                if (current is not null && current != whole) { yield return (buffer.ToString(), current); buffer.Clear(); }
                current = whole; buffer.Append(value);
            }
            else
            {
                foreach (Rune rune in value.EnumerateRunes())
                {
                    SKFont chosen = Resolve(rune.Value, font);
                    if (current is not null && current != chosen) { yield return (buffer.ToString(), current); buffer.Clear(); }
                    current = chosen; buffer.Append(rune.ToString());
                }
            }
        }
        if (buffer.Length > 0 && current is not null) yield return (buffer.ToString(), current);
    }

    private static bool Ignorable(Rune rune) => Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format
        || rune.Value is >= 0xfe00 and <= 0xfe0f or >= 0xe0100 and <= 0xe01ef;

    private SKFont Resolve(int character, SKFont font)
    {
        if (font.ContainsGlyph(character)) return font;
        if (_symbols.ContainsGlyph(character)) return Styled(font, _symbols);
        if (!_characters.TryGetValue(character, out SKTypeface? face))
        {
            face = Own(SKFontManager.Default.MatchCharacter(character));
            // Some Skia builds enumerate installed families but provide no character matcher.
            // Query installed Unicode families explicitly in that case (no filesystem scan).
            if (face is null || !face.ContainsGlyph(character))
                face = KnownFallbacks().FirstOrDefault(candidate => candidate.ContainsGlyph(character));
            if (_characters.Count < MaxCharacters) _characters.Add(character, face);
        }
        return face is null ? font : Styled(font, face);
    }

    private SKTypeface[] KnownFallbacks()
    {
        if (_knownFallbacks is not null) return _knownFallbacks;
        string[] preferred = ["Noto Sans Symbols 2", "Noto Sans Symbols", "Noto Color Emoji", "Segoe UI Emoji", "Apple Color Emoji",
            "Noto Sans CJK SC", "Noto Sans CJK JP", "Droid Sans Fallback", "Arial Unicode MS", "Segoe UI Symbol", "Symbola", "DejaVu Sans"];
        var installed = SKFontManager.Default.FontFamilies.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _knownFallbacks = preferred.Where(installed.Contains).Select(name => Own(SKTypeface.FromFamilyName(name)))
            .OfType<SKTypeface>().ToArray();
        return _knownFallbacks;
    }

    private SKTypeface? Own(SKTypeface? face)
    {
        if (face is null) return null;
        nint id = face.Handle;
        if (_faces.TryGetValue(id, out var existing))
        { if (!ReferenceEquals(face, existing)) face.Dispose(); return existing; }
        if (_faces.Count < MaxFaces) { _faces.Add(id, face); return face; }
        face.Dispose(); return null;
    }

    private SKFont Styled(SKFont source, SKTypeface face)
    {
        var key = (source, face.Handle);
        if (!_fonts.TryGetValue(key, out var result))
        {
            result = new(face, source.Size) { Edging = source.Edging, Embolden = source.Embolden,
                SkewX = source.SkewX, ScaleX = source.ScaleX, Subpixel = source.Subpixel, Hinting = source.Hinting };
            _fonts.Add(key, result);
        }
        return result;
    }

    public void Dispose()
    {
        foreach (var font in _fonts.Values) font.Dispose();
        foreach (var face in _faces.Values) face.Dispose();
        _symbols.Dispose();
    }
}
