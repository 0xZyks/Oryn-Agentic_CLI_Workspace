using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;

namespace Oryn.App;

/// <summary>Owned loopback process, authenticated readiness and bounded startup/shutdown.</summary>
public sealed class LlamaCppRuntime : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task _stdout, _stderr;
    private readonly ConcurrentQueue<string> _diagnostics;
    private int _disposed;
    private LlamaCppRuntime(Process process, IInferenceProvider provider, Task stdout, Task stderr, ConcurrentQueue<string> diagnostics)
    { _process = process; Provider = provider; _stdout = stdout; _stderr = stderr; _diagnostics = diagnostics; }
    public IInferenceProvider Provider { get; }
    public int ProcessId => _process.Id;
    public bool IsAlive => Volatile.Read(ref _disposed) == 0 && !_process.HasExited;
    public async Task<string?> FailureDiagnosticAsync()
    {
        if (Volatile.Read(ref _disposed) != 0 || !_process.HasExited) return null;
        try { await Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (TimeoutException) { }
        return $"llama-server arrêté (code {_process.ExitCode}).\n" + string.Join("\n", _diagnostics);
    }

    public static async Task<LlamaCppRuntime> StartAsync(string projectRoot, WorkspaceSettings settings, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        settings.Validate();
        ModelProfile profile = settings.SelectedProfile ?? throw new IOException("Aucun modèle configuré. Ouvrez le setup d'inférence.");
        profile.Validate();
        string model = InferenceInstallation.ModelPath(projectRoot, settings, profile);
        if (!File.Exists(model)) throw new IOException($"Modèle introuvable : {model}");
        Uri endpoint = new(settings.InferenceEndpoint);
        var probe = new TcpListener(IPAddress.Loopback, endpoint.Port);
        try { probe.Server.ExclusiveAddressUse = true; probe.Start(); }
        catch (SocketException) { throw new IOException($"Port {endpoint.Port} déjà occupé. Aucun serveur externe n'a été adopté."); }
        finally { probe.Stop(); }
        string alias = "oryn-" + Guid.NewGuid().ToString("N");
        string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        ProcessStartInfo start = CreateStart(projectRoot, settings, profile, model, endpoint.Port, alias);
        start.Environment["LLAMA_API_KEY"] = key;
        Process? process = null;
        HttpClient? http = null;
        ConcurrentQueue<string> tail = new();
        Task stdout = Task.CompletedTask, stderr = Task.CompletedTask;
        try
        {
            token.ThrowIfCancellationRequested();
            process = Process.Start(start) ?? throw new IOException("Impossible de démarrer llama-server.");
            stdout = DrainAsync(process.StandardOutput, tail, key); stderr = DrainAsync(process.StandardError, tail, key);
            http = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var startup = CancellationTokenSource.CreateLinkedTokenSource(token);
            startup.CancelAfter(TimeSpan.FromSeconds(settings.StartupTimeoutSeconds));
            while (!process.HasExited)
            {
                startup.Token.ThrowIfCancellationRequested();
                using var requestDeadline = CancellationTokenSource.CreateLinkedTokenSource(startup.Token);
                requestDeadline.CancelAfter(TimeSpan.FromSeconds(2));
                try
                {
                    // /health is public. A protected random alias proves this is our process, not a port race.
                    using var response = await http.GetAsync(new Uri(endpoint, "/v1/models"), requestDeadline.Token);
                    if (response.IsSuccessStatusCode)
                    {
                        using var models = JsonDocument.Parse(await response.Content.ReadAsStringAsync(requestDeadline.Token));
                        if (models.RootElement.GetProperty("data").EnumerateArray().Any(m => m.GetProperty("id").GetString() == alias) && !process.HasExited)
                            return new(process, new LlamaCppProvider(http, endpoint, profile, alias, Path.GetFileName(model)), stdout, stderr, tail);
                    }
                }
                catch (HttpRequestException) { }
                catch (OperationCanceledException) when (!startup.IsCancellationRequested) { }
                await Task.Delay(200, startup.Token);
            }
            // A fast failure can exit before the redirected stderr reader has caught up.
            try { await Task.WhenAll(stdout, stderr).WaitAsync(TimeSpan.FromSeconds(2), token); }
            catch (TimeoutException) { /* Preserve the exit code and diagnostics already captured. */ }
            string diagnostics = string.Join("\n", tail);
            string hint = diagnostics.Contains("out of memory", StringComparison.OrdinalIgnoreCase)
                && diagnostics.Contains("cuda", StringComparison.OrdinalIgnoreCase)
                ? $"Mémoire GPU insuffisante (VRAM). Profil {settings.SelectedModelProfile} : contexte {profile.ContextTokens}, " +
                  $"FlashAttention={profile.FlashAttention}, CpuMoeLayers={profile.CpuMoeLayers}. " +
                  "Libérez de la VRAM ou ajustez le placement CPU/GPU dans oryn.settings.json ; aucun basculement automatique n'a été effectué.\n"
                : "";
            throw new IOException(hint + $"llama-server arrêté (code {process.ExitCode}).\n" + diagnostics);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        { if (process is not null) await StopProcessAsync(process); http?.Dispose(); throw new IOException("Délai de chargement llama.cpp dépassé."); }
        catch
        { if (process is not null) await StopProcessAsync(process); http?.Dispose(); throw; }
    }

    private static ProcessStartInfo CreateStart(string root, WorkspaceSettings settings, ModelProfile profile, string model, int port, string alias)
    {
        string? runner = settings.LlamaServerPath is null ? settings.InferenceRunnerPath : null;
        string? server = settings.LlamaServerPath;
        if (server is not null) server = Path.GetFullPath(server, root);
        if (server is null && runner is null) server = InferenceInstallation.FindServer(root, settings);
        if (server is null && runner is null && OperatingSystem.IsLinux())
        {
            string? development = InferenceInstallation.DevelopmentRoot;
            if (development is not null) runner = Path.Combine(development, "scripts/inference/run.py");
        }
        ProcessStartInfo start;
        if (runner is not null)
        {
            string path = Path.GetFullPath(runner, root);
            if (!File.Exists(path)) throw new IOException($"Lanceur introuvable : {path}");
            start = new(settings.PythonExecutable ?? (OperatingSystem.IsWindows() ? "python" : "python3"));
            start.ArgumentList.Add(path);
            Add(start, "--context", profile.ContextTokens); Add(start, "--gpu-layers", profile.GpuLayers);
            Add(start, "--cpu-moe", profile.CpuMoeLayers); Add(start, "--flash", profile.FlashAttention);
        }
        else
        {
            if (server is null || !File.Exists(server)) throw new IOException("Backend absent : ouvrez le setup pour l'installer ou configurez LlamaServerPath.");
            start = new(server) { WorkingDirectory = Path.GetDirectoryName(server)! };
            Add(start, "--host", "127.0.0.1"); Add(start, "--ctx-size", profile.ContextTokens);
            Add(start, "--n-gpu-layers", profile.GpuLayers); Add(start, "--parallel", 1);
            Add(start, "--n-cpu-moe", profile.CpuMoeLayers); Add(start, "--flash-attn", profile.FlashAttention);
            Add(start, "--batch-size", 512); Add(start, "--ubatch-size", 128);
            Add(start, "--fit", "off"); Add(start, "--cache-ram", 0);
            Add(start, "--reasoning-format", "deepseek");
            start.ArgumentList.Add("--jinja"); start.ArgumentList.Add("--offline"); start.ArgumentList.Add("--no-ui");
            string directory = InferenceInstallation.LibraryRoot(server);
            string[] libraries = Directory.GetFiles(directory, "*.so*", SearchOption.AllDirectories).Select(Path.GetDirectoryName).OfType<string>().Distinct().ToArray();
            if (OperatingSystem.IsLinux() && libraries.Length > 0)
                start.Environment["LD_LIBRARY_PATH"] = string.Join(Path.PathSeparator, libraries) + Path.PathSeparator + Environment.GetEnvironmentVariable("LD_LIBRARY_PATH");
        }
        Add(start, "--model", model); Add(start, "--port", port); Add(start, "--alias", alias);
        Add(start, "--reasoning", profile.Reasoning ? "on" : "off"); Add(start, "--reasoning-budget", profile.ReasoningBudget);
        Add(start, "--reasoning-effort", profile.ReasoningEffort);
        start.UseShellExecute = false; start.RedirectStandardError = true; start.RedirectStandardOutput = true; start.CreateNoWindow = true;
        return start;
    }
    private static void Add(ProcessStartInfo start, string name, object value)
    { start.ArgumentList.Add(name); start.ArgumentList.Add(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)!); }
    private static async Task DrainAsync(StreamReader reader, ConcurrentQueue<string> tail, string key)
    {
        try
        {
            while (await reader.ReadLineAsync() is { } line)
            {
                string masked = line.Replace(key, "[masked]");
                tail.Enqueue(masked[..Math.Min(masked.Length, 1024)]);
                while (tail.Count > 20) tail.TryDequeue(out _);
            }
        }
        catch (ObjectDisposedException) { }
        catch (IOException) { }
    }
    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { process.Dispose(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await Provider.DisposeAsync();
        await StopProcessAsync(_process);
        await Task.WhenAll(_stdout, _stderr).WaitAsync(TimeSpan.FromSeconds(2));
    }
}
