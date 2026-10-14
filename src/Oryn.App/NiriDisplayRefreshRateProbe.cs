using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Oryn.App;

/// <summary>Read-only Niri IPC, independent of Terminal.Gui. No focus-based window guessing.</summary>
public sealed class NiriDisplayRefreshRateProbe(string socketPath, IReadOnlyList<int> processIds) : IDisplayRefreshRateProbe
{
    private ulong? _windowId;

    public static IDisplayRefreshRateProbe ForCurrentTerminal()
    {
        if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("NIRI_SOCKET") is { Length: > 0 } path
            && Environment.GetEnvironmentVariable("SSH_CONNECTION") is null
            && Environment.GetEnvironmentVariable("SSH_TTY") is null
            && Environment.GetEnvironmentVariable("TMUX") is null
            && Environment.GetEnvironmentVariable("STY") is null)
            return new NiriDisplayRefreshRateProbe(path, GetProcessIds());
        return new UnavailableProbe();
    }

    public async Task<DisplayRefreshRateReading> ReadAsync(CancellationToken cancellationToken)
    {
        if (processIds.Count == 0) return new(null, "Terminal process is unknown");
        using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(750));
        try
        {
            // Each request is bounded. Niri snapshots are not atomic; the next poll
            // reconciles a window/workspace move that occurs between these replies.
            Task<JsonDocument> windowsTask = RequestAsync("Windows", timeout.Token);
            Task<JsonDocument> workspacesTask = RequestAsync("Workspaces", timeout.Token);
            Task<JsonDocument> outputsTask = RequestAsync("Outputs", timeout.Token);
            // Dispose successful replies even if another request fails.
            try { await Task.WhenAll(windowsTask, workspacesTask, outputsTask).ConfigureAwait(false); }
            finally
            {
                if (!windowsTask.IsCompletedSuccessfully || !workspacesTask.IsCompletedSuccessfully || !outputsTask.IsCompletedSuccessfully)
                {
                    if (windowsTask.IsCompletedSuccessfully) windowsTask.Result.Dispose();
                    if (workspacesTask.IsCompletedSuccessfully) workspacesTask.Result.Dispose();
                    if (outputsTask.IsCompletedSuccessfully) outputsTask.Result.Dispose();
                }
            }
            using JsonDocument windows = windowsTask.Result;
            using JsonDocument workspaces = workspacesTask.Result;
            using JsonDocument outputs = outputsTask.Result;
            DisplayRefreshRateReading result = Resolve(windows.RootElement, workspaces.RootElement,
                outputs.RootElement, processIds, _windowId);
            if (result.Display is { } display) _windowId = display.WindowId;
            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(null, "Niri detection timed out"); }
        catch (Exception ex) when (ex is SocketException or IOException or JsonException or InvalidOperationException
            or ArgumentException or UnauthorizedAccessException or FormatException or OverflowException or KeyNotFoundException)
        { return new(null, $"Niri detection unavailable or invalid reply ({ex.GetType().Name})"); }
    }

    public static DisplayRefreshRateReading Resolve(JsonElement windows, JsonElement workspaces, JsonElement outputs,
        IReadOnlyList<int> processIds, ulong? trackedWindowId = null)
    {
        JsonElement? terminal = null;
        foreach (int pid in processIds)
        {
            JsonElement[] matches = windows.EnumerateArray().Where(window =>
                window.TryGetProperty("pid", out JsonElement id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out int value) && value == pid
                && (trackedWindowId is null || window.GetProperty("id").GetUInt64() == trackedWindowId)).ToArray();
            if (matches.Length > 1) return new(null, "Multiple terminal windows share the same process");
            if (matches.Length == 1) { terminal = matches[0]; break; }
        }
        if (terminal is not { } selected) return new(null, "Terminal window not found");
        if (!selected.TryGetProperty("workspace_id", out JsonElement workspaceId) || workspaceId.ValueKind != JsonValueKind.Number
            || !workspaceId.TryGetUInt64(out ulong workspace))
            return new(null, "Terminal workspace is unknown");
        foreach (JsonElement item in workspaces.EnumerateArray())
        {
            if (item.GetProperty("id").GetUInt64() != workspace) continue;
            string? outputName = item.GetProperty("output").GetString();
            if (outputName is null || !outputs.TryGetProperty(outputName, out JsonElement output)
                || output.GetProperty("current_mode").ValueKind != JsonValueKind.Number
                || !output.GetProperty("current_mode").TryGetInt32(out int index))
                return new(null, "Terminal output is unavailable");
            JsonElement modes = output.GetProperty("modes");
            if (index < 0 || index >= modes.GetArrayLength()) return new(null, "Invalid output mode");
            double hertz = modes[index].GetProperty("refresh_rate").GetDouble() / 1000;
            if (!double.IsFinite(hertz) || hertz < 1 || hertz > 500) return new(null, "Invalid monitor refresh rate");
            return new(new(hertz, outputName, "niri", selected.GetProperty("id").GetUInt64()));
        }
        return new(null, "Terminal workspace not found");
    }

    private async Task<JsonDocument> RequestAsync(string request, CancellationToken token)
    {
        using Socket socket = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        await socket.ConnectAsync(new UnixDomainSocketEndPoint(socketPath), token).ConfigureAwait(false);
        using NetworkStream stream = new(socket);
        await stream.WriteAsync(Encoding.UTF8.GetBytes($"\"{request}\"\n"), token).ConfigureAwait(false);
        using MemoryStream reply = new();
        byte[] buffer = new byte[4096];
        while (reply.Length < 1048576)
        {
            int count = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (count == 0) throw new IOException("Incomplete Niri reply");
            int newline = Array.IndexOf(buffer, (byte)'\n', 0, count);
            reply.Write(buffer, 0, newline < 0 ? count : newline);
            if (reply.Length > 1048576) break;
            if (newline < 0) continue;
            using JsonDocument envelope = JsonDocument.Parse(reply.ToArray());
            if (!envelope.RootElement.TryGetProperty("Ok", out JsonElement ok)
                || !ok.TryGetProperty(request, out JsonElement result)) throw new JsonException("Unexpected Niri reply");
            return JsonDocument.Parse(result.GetRawText());
        }
        throw new IOException("Niri reply exceeds 1 MiB");
    }

    private static IReadOnlyList<int> GetProcessIds()
    {
        List<int> ids = [];
        // Kitty explicitly exports the PID of the hosting terminal. With a shared
        // process and multiple OS windows we refuse to guess which one hosts Oryn.
        if (int.TryParse(Environment.GetEnvironmentVariable("KITTY_PID"), NumberStyles.None,
            CultureInfo.InvariantCulture, out int kittyPid) && kittyPid > 0) ids.Add(kittyPid);
        int pid = Environment.ProcessId;
        HashSet<int> visited = [];
        try
        {
            while (pid > 1 && visited.Add(pid) && visited.Count <= 64)
            {
                if (!ids.Contains(pid)) ids.Add(pid);
                string stat = File.ReadAllText($"/proc/{pid}/stat");
                int end = stat.LastIndexOf(')');
                if (end < 0) break;
                string[] fields = stat[(end + 2)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length < 2 || !int.TryParse(fields[1], CultureInfo.InvariantCulture, out pid)) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return ids;
    }

    private sealed class UnavailableProbe : IDisplayRefreshRateProbe
    {
        public Task<DisplayRefreshRateReading> ReadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DisplayRefreshRateReading(null, "Automatic detection supports local Niri sessions"));
    }
}
