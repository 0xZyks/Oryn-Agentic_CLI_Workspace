namespace Oryn.App;

public sealed record DisplayRefreshRate(double Hertz, string Output, string Source, ulong WindowId);
public sealed record DisplayRefreshRateReading(DisplayRefreshRate? Display, string? UnavailableReason = null);

public interface IDisplayRefreshRateProbe
{
    Task<DisplayRefreshRateReading> ReadAsync(CancellationToken cancellationToken);
}

/// <summary>Rendering policy independent of the terminal renderer. Update on the UI thread.</summary>
public sealed class RenderCadence(int? manualFramesPerSecond)
{
    public const int FallbackFramesPerSecond = 60;
    public int? ManualFramesPerSecond { get; private set; } = Checked(manualFramesPerSecond);
    public DisplayRefreshRateReading Reading { get; private set; } = new(null,
        manualFramesPerSecond is null ? "Detection pending" : "Automatic detection disabled in manual mode");
    public int FramesPerSecond => ManualFramesPerSecond ?? (Reading.Display is { } display
        ? Math.Clamp((int)Math.Round(display.Hertz), 1, 500) : FallbackFramesPerSecond);
    public string Description => ManualFramesPerSecond is not null ? $"{FramesPerSecond} Hz manual"
        : Reading.Display is { } display ? $"{FramesPerSecond} Hz auto ({display.Output})" : "60 Hz fallback";

    public void SetManual(int? framesPerSecond)
    {
        Validate(framesPerSecond);
        if (ManualFramesPerSecond is not null && framesPerSecond is null)
            Reading = new(null, "Detection pending");
        else if (framesPerSecond is not null)
            Reading = new(null, "Automatic detection disabled in manual mode");
        ManualFramesPerSecond = framesPerSecond;
    }

    public bool Apply(DisplayRefreshRateReading reading)
    {
        if (ManualFramesPerSecond is not null || reading == Reading) return false;
        if (reading.Display is { } display && (!double.IsFinite(display.Hertz) || display.Hertz < 1 || display.Hertz > 500))
            reading = new(null, "Invalid monitor refresh rate");
        if (reading == Reading) return false;
        Reading = reading;
        return true;
    }

    public static void Validate(int? framesPerSecond)
    {
        if (framesPerSecond is < 1 or > 500)
            throw new ArgumentException("Render cadence must be auto or an integer from 1 to 500.");
    }

    private static int? Checked(int? framesPerSecond)
    {
        Validate(framesPerSecond);
        return framesPerSecond;
    }
}
