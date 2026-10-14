using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Oryn.App;
using Xunit;

namespace Oryn.App.Tests;

public sealed class RenderCadenceTests
{
    private const string Windows = """
        [{"id":7,"pid":123,"workspace_id":1,"is_focused":false},
         {"id":8,"pid":456,"workspace_id":2,"is_focused":true},
         {"id":9,"pid":null,"workspace_id":null}]
        """;
    private const string Workspaces = """
        [{"id":1,"output":"DP-1"},{"id":2,"output":"HDMI-1"}]
        """;
    private const string Outputs = """
        {"DP-1":{"current_mode":1,"modes":[{"refresh_rate":60000},{"refresh_rate":143998}]},
         "HDMI-1":{"current_mode":0,"modes":[{"refresh_rate":59940}]}}
        """;

    [Fact]
    public void MatchesHostingProcessAndCurrentModeInsteadOfFocusedWindow()
    {
        DisplayRefreshRateReading reading = Resolve(Windows, Workspaces, Outputs);
        Assert.Equal(new DisplayRefreshRate(143.998, "DP-1", "niri", 7), reading.Display);
        RenderCadence cadence = new(null);
        cadence.Apply(reading);
        Assert.Equal(144, cadence.FramesPerSecond);
        Assert.Contains("auto", cadence.Description);
    }

    [Fact]
    public void AmbiguousProcessNeverChoosesFocusedWindow()
    {
        string windows = Windows.Replace("\"pid\":456", "\"pid\":123");
        Assert.Null(Resolve(windows, Workspaces, Outputs).Display);
        Assert.Equal(7UL, Resolve(windows, Workspaces, Outputs, 7).Display?.WindowId);
        Assert.Null(Resolve(windows, Workspaces, Outputs, 99).Display);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("-1")]
    [InlineData("99")]
    public void UnavailableOrInvalidCurrentModeFallsBack(string mode)
    {
        Assert.Null(Resolve(Windows, Workspaces, Outputs.Replace("\"current_mode\":1", $"\"current_mode\":{mode}")).Display);
    }

    [Fact]
    public void WindowOnAnotherWorkspaceChangesCadenceAndMissingWindowResetsIt()
    {
        RenderCadence cadence = new(null);
        cadence.Apply(Resolve(Windows, Workspaces, Outputs));
        cadence.Apply(Resolve(Windows.Replace("\"workspace_id\":1", "\"workspace_id\":2"), Workspaces, Outputs, 7));
        Assert.Equal(60, cadence.FramesPerSecond);
        Assert.Equal("HDMI-1", cadence.Reading.Display?.Output);
        cadence.Apply(Resolve("[]", Workspaces, Outputs, 7));
        Assert.Equal(60, cadence.FramesPerSecond);
        Assert.Contains("fallback", cadence.Description);
    }

    [Fact]
    public void ManualCadenceIgnoresBackgroundResultsAndAutoStartsFresh()
    {
        RenderCadence cadence = new(null);
        cadence.Apply(Resolve(Windows, Workspaces, Outputs));
        cadence.SetManual(120);
        Assert.False(cadence.Apply(new(null, "Disconnected")));
        Assert.Equal(120, cadence.FramesPerSecond);
        cadence.SetManual(null);
        Assert.Equal(60, cadence.FramesPerSecond);
        Assert.Null(cadence.Reading.Display);
        cadence.Apply(Resolve(Windows, Workspaces, Outputs));
        Assert.Equal(144, cadence.FramesPerSecond);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(501)]
    public void RejectsInvalidManualCadence(int rate)
    {
        Assert.Throws<ArgumentException>(() => new RenderCadence(rate));
        Assert.Throws<ArgumentException>(() => new RenderCadence(null).SetManual(rate));
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--fps", rate.ToString()], Path.GetTempPath()));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("144.5")]
    [InlineData("-60")]
    public void RejectsMalformedCommandLineCadence(string rate) =>
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--fps", rate], Path.GetTempPath()));

    [Fact]
    public void CommandLineAutoExplicitlyOverridesConfiguration()
    {
        Assert.Throws<ArgumentException>(() => StartupOptions.Parse(["--fps"], Path.GetTempPath()));
        StartupOptions auto = StartupOptions.Parse(["--fps", "auto"], Path.GetTempPath());
        Assert.True(auto.OverrideRenderCadence);
        Assert.Null(auto.RenderFramesPerSecond);
        Assert.Equal(144, StartupOptions.Parse(["--fps", "144"], Path.GetTempPath()).RenderFramesPerSecond);
        Assert.False(StartupOptions.Parse([], Path.GetTempPath()).OverrideRenderCadence);
    }

    [Fact]
    public void SettingsValidateCadence()
    {
        string path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "{\"RenderFramesPerSecond\":144}");
            Assert.Equal(144, WorkspaceSettings.Load(path).RenderFramesPerSecond);
            File.WriteAllText(path, "{\"RenderFramesPerSecond\":null}");
            Assert.Null(WorkspaceSettings.Load(path).RenderFramesPerSecond);
            File.WriteAllText(path, "{\"RenderFramesPerSecond\":501}");
            Assert.Throws<ArgumentException>(() => WorkspaceSettings.Load(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task SocketProbeTracksWindowAcrossMovesAndHandlesBadReplies()
    {
        if (!OperatingSystem.IsLinux()) return;
        string scene = Windows;
        bool malformed = false;
        await using FakeNiri server = new(request => malformed ? "not-json\n" : Envelope(request, request switch
        {
            "Windows" => scene,
            "Workspaces" => Workspaces,
            "Outputs" => Outputs,
            _ => throw new InvalidOperationException()
        }));
        NiriDisplayRefreshRateProbe probe = new(server.Path, [123]);
        DisplayRefreshRateReading initial = await probe.ReadAsync(default);
        Assert.True(initial.Display is not null, initial.UnavailableReason);
        Assert.Equal(143.998, initial.Display?.Hertz);
        scene = Windows.Replace("\"workspace_id\":1", "\"workspace_id\":2");
        Assert.Equal(59.94, (await probe.ReadAsync(default)).Display?.Hertz);
        scene = "[{\"id\":55,\"pid\":123,\"workspace_id\":1}]";
        Assert.Null((await probe.ReadAsync(default)).Display); // Never silently switch to another window.
        malformed = true;
        Assert.Null((await probe.ReadAsync(default)).Display);
    }

    [Fact]
    public async Task ProbeFailureCancellationAndOversizedRepliesAreBounded()
    {
        if (!OperatingSystem.IsLinux()) return;
        NiriDisplayRefreshRateProbe missing = new("/tmp/oryn-missing-" + Guid.NewGuid(), [123]);
        Assert.Null((await missing.ReadAsync(default)).Display);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => missing.ReadAsync(cancellation.Token));
        await using FakeNiri oversized = new(_ => new string('x', 1048577) + "\n");
        Assert.Null((await new NiriDisplayRefreshRateProbe(oversized.Path, [123]).ReadAsync(default)).Display);
        await using FakeNiri stalled = new(_ => null);
        DisplayRefreshRateReading timedOut = await new NiriDisplayRefreshRateProbe(stalled.Path, [123]).ReadAsync(default);
        Assert.Contains("timed out", timedOut.UnavailableReason);
    }

    private static DisplayRefreshRateReading Resolve(string windows, string workspaces, string outputs, ulong? id = null)
    {
        using JsonDocument w = JsonDocument.Parse(windows);
        using JsonDocument s = JsonDocument.Parse(workspaces);
        using JsonDocument o = JsonDocument.Parse(outputs);
        return NiriDisplayRefreshRateProbe.Resolve(w.RootElement, s.RootElement, o.RootElement, [123], id);
    }

    private static string Envelope(string request, string payload)
    {
        using JsonDocument document = JsonDocument.Parse(payload);
        return JsonSerializer.Serialize(new Dictionary<string, object>
            { ["Ok"] = new Dictionary<string, JsonElement> { [request] = document.RootElement } }) + "\n";
    }

    private sealed class FakeNiri : IAsyncDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "oryn-ipc-" + Guid.NewGuid());
        private readonly Socket _listener = new(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _task;

        public FakeNiri(Func<string, string?> response)
        {
            _listener.Bind(new UnixDomainSocketEndPoint(Path));
            _listener.Listen(8);
            _task = ServeAsync(response);
        }

        private async Task ServeAsync(Func<string, string?> response)
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    using Socket client = await _listener.AcceptAsync(_stop.Token);
                    using NetworkStream stream = new(client);
                    using StreamReader reader = new(stream, leaveOpen: true);
                    string? request = await reader.ReadLineAsync(_stop.Token);
                    string? reply = response(JsonSerializer.Deserialize<string>(request!)!);
                    if (reply is null) await Task.Delay(Timeout.Infinite, _stop.Token);
                    else
                    {
                        try { await stream.WriteAsync(Encoding.UTF8.GetBytes(reply), _stop.Token); }
                        catch (IOException) { } // Bounded client may close before an oversized reply finishes.
                    }
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
            catch (SocketException) when (_stop.IsCancellationRequested) { }
        }

        public async ValueTask DisposeAsync()
        {
            _stop.Cancel();
            _listener.Dispose();
            await _task;
            _stop.Dispose();
            File.Delete(Path);
        }
    }
}
