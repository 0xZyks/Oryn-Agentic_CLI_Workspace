using System.Globalization;
using System.Text.Json;

namespace Oryn.App;

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public static RgbColor Parse(string? value)
    {
        if (value is null || value.Length != 7 || value[0] != '#'
            || !uint.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
            throw new ArgumentException("Use a color in #RRGGBB format.");
        return new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
    public (float Hue, float Saturation, float Value) Hsv
    {
        get
        {
            float r = R / 255f, g = G / 255f, b = B / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), d = max - min;
            float hue = d == 0 ? 0 : max == r ? 60 * ((g - b) / d % 6) : max == g ? 60 * ((b - r) / d + 2) : 60 * ((r - g) / d + 4);
            return ((hue + 360) % 360, max == 0 ? 0 : d / max, max);
        }
    }
    public static RgbColor FromHsv(float hue, float saturation, float value)
    {
        hue = (hue % 360 + 360) % 360; saturation = Math.Clamp(saturation, 0, 1); value = Math.Clamp(value, 0, 1);
        float c = value * saturation, x = c * (1 - Math.Abs(hue / 60 % 2 - 1)), m = value - c;
        (float r, float g, float b) = hue switch
        { < 60 => (c, x, 0f), < 120 => (x, c, 0f), < 180 => (0f, c, x), < 240 => (0f, x, c), < 300 => (x, 0f, c), _ => (c, 0f, x) };
        return new((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
}

public sealed record AppearanceSettings
{
    public string AccentColor { get; init; } = "#B49AF2";
    public string ThemeColor { get; init; } = "#090F16";
    public AppearanceSettings Validated() => this with
    { AccentColor = RgbColor.Parse(AccentColor).Hex, ThemeColor = RgbColor.Parse(ThemeColor).Hex };
}

public static class AppearanceStore
{
    public static string DefaultPath => Path.Combine(Path.GetDirectoryName(KeyBindingStore.DefaultPath)!, "appearance.json");
    public static AppearanceSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException("Appearance profile exceeds 4 KiB.");
        return (JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(path), new JsonSerializerOptions
        { PropertyNameCaseInsensitive = true, UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Appearance profile must be an object.")).Validated();
    }
    public static void Save(string path, AppearanceSettings settings)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings.Validated(), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, full, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
