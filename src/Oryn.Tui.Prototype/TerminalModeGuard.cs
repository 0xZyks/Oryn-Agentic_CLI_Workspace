using System.ComponentModel;
using System.Diagnostics;

namespace Oryn.Tui.Prototype;

/// <summary>POSIX-only backup around the provisional library's terminal lifecycle.</summary>
internal sealed class TerminalModeGuard(string command, string? snapshot) : IDisposable
{
    public static TerminalModeGuard Capture()
    {
        if (OperatingSystem.IsWindows()) return new("", null);
        string command = File.Exists("/bin/stty") ? "/bin/stty" : "/usr/bin/stty";
        try { return new(command, Run(command, "-g")); }
        catch (Exception ex) when (ex is Win32Exception or IOException) { return new(command, null); }
    }

    public void Dispose()
    {
        if (snapshot is not null) Run(command, snapshot);
    }

    private static string Run(string command, string argument)
    {
        ProcessStartInfo start = new(command)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
            // stdin is inherited from the terminal being protected.
        };
        start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new IOException("Could not start terminal mode backup.");
        if (!process.WaitForExit(1000))
        {
            process.Kill();
            throw new IOException("Terminal mode backup timed out.");
        }
        if (process.ExitCode != 0) throw new IOException("Could not read or restore terminal input modes.");
        return process.StandardOutput.ReadToEnd().Trim();
    }
}
