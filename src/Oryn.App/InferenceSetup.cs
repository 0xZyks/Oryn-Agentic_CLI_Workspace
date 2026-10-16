using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Oryn.App;

public sealed record GpuMemory(string Name, long TotalBytes, long AvailableBytes);
public sealed record HardwareSnapshot(string Platform, int LogicalProcessors, long? RamTotalBytes,
    long? RamAvailableBytes, long? DiskAvailableBytes, IReadOnlyList<GpuMemory> Gpus, string Notes);
public sealed record ModelSuggestion(CatalogModel Model, string Slot, bool Gpu, bool Recommended, string Reason, int CpuMoeLayers = 0)
{
    public ModelProfile Profile => new() { CatalogId = Model.Id, GpuLayers = Gpu ? 99 : 0,
        CpuMoeLayers = CpuMoeLayers, FlashAttention = CpuMoeLayers > 0 ? "on" : "auto",
        Reasoning = Model.Id == "gptoss20b-mxfp4", ReasoningBudget = 256, ReasoningEffort = "low",
        Temperature = Model.Id == "gptoss20b-mxfp4" ? 1.0 : 0.7,
        TopP = Model.Id == "gptoss20b-mxfp4" ? 1.0 : 0.8,
        TopK = Model.Id == "gptoss20b-mxfp4" ? 0 : 20,
        PresencePenalty = Model.Id == "gptoss20b-mxfp4" ? 0 : 1.5 };
}

public sealed class InferenceSetupState
{
    public HardwareSnapshot? Hardware { get; internal set; }
    public IReadOnlyList<ModelSuggestion> Suggestions { get; internal set; } = [];
    public int Selection { get; set; }
    public bool Busy { get; internal set; }
    public bool ConfirmDownload { get; internal set; }
    public string Status { get; internal set; } = "Détection matérielle locale…";
    public ModelSuggestion? Selected => Suggestions.Count == 0 ? null : Suggestions[Math.Clamp(Selection, 0, Suggestions.Count - 1)];
    public void Refresh(HardwareSnapshot hardware, IReadOnlyList<ModelSuggestion> suggestions, string? activeCatalogId)
    {
        Hardware = hardware; Suggestions = suggestions;
        int selected = suggestions.ToList().FindIndex(s => s.Model.Id == activeCatalogId);
        Selection = Math.Max(0, selected); ConfirmDownload = false;
    }
}

public static class HardwareProbe
{
    private const long GiB = 1024L * 1024 * 1024;
    public static async Task<HardwareSnapshot> DetectAsync(string assets, CancellationToken token)
    {
        long? total = null, available = null, disk = null;
        string notes = "Suggestions à confirmer par un essai ; mémoire inconnue conservée comme inconnue.";
        try
        {
            if (OperatingSystem.IsLinux())
            {
                var memory = File.ReadLines("/proc/meminfo").Select(line => line.Split(':', 2))
                    .ToDictionary(p => p[0], p => long.Parse(p[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0]) * 1024);
                total = memory.TryGetValue("MemTotal", out long installed) ? installed : null;
                available = memory.TryGetValue("MemAvailable", out long free) ? free : null;
                long limit = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
                if (limit > 0 && total > limit) { total = limit; if (available is { } known) available = Math.Min(known, limit); notes += " Limite mémoire du runtime prise en compte."; }
            }
            else if (OperatingSystem.IsWindows())
            {
                var status = new MemoryStatus { Length = (uint)Marshal.SizeOf<MemoryStatus>() };
                if (GlobalMemoryStatusEx(ref status)) { total = (long)status.TotalPhysical; available = (long)status.AvailablePhysical; }
            }
            else if (OperatingSystem.IsMacOS())
            {
                string value = await CaptureAsync("sysctl", ["-n", "hw.memsize"], token);
                if (long.TryParse(value.Trim(), out long bytes)) total = bytes;
                notes += " Apple Silicon : mémoire unifiée ; pas d'ajout d'une VRAM fictive à la RAM.";
            }
            string? path = Path.GetPathRoot(Path.GetFullPath(assets));
            if (path is not null) disk = new DriveInfo(path).AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or TimeoutException or System.ComponentModel.Win32Exception)
        { notes += " Détection partielle : " + ex.Message; }
        List<GpuMemory> gpus = [];
        try
        {
            string value = await CaptureAsync("nvidia-smi", ["--query-gpu=name,memory.total,memory.free", "--format=csv,noheader,nounits"], token);
            foreach (string line in value.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split(',');
                if (fields.Length == 3 && long.TryParse(fields[1].Trim(), out long size) && long.TryParse(fields[2].Trim(), out long free))
                    gpus.Add(new(fields[0].Trim(), size * 1024 * 1024, free * 1024 * 1024));
            }
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception or TimeoutException) { }
        return new(RuntimeInformation.RuntimeIdentifier, Environment.ProcessorCount, total, available, disk, gpus, notes);
    }
    public static IReadOnlyList<ModelSuggestion> Suggest(HardwareSnapshot machine)
    {
        bool nvidia = machine.Gpus.Count > 0 && machine.Platform.StartsWith("linux-", StringComparison.Ordinal);
        bool metal = machine.Platform.StartsWith("osx-arm64", StringComparison.Ordinal);
        long freeGpu = machine.Gpus.Count == 0 ? 0 : machine.Gpus.Max(g => g.AvailableBytes);
        long freeRam = machine.RamAvailableBytes ?? 0;
        List<ModelSuggestion> result = [];
        foreach (string id in new[] { "4b-q4", "8b-q4", "gptoss20b-mxfp4" })
        {
            var model = InferenceInstallation.Models.Single(m => m.Id == id);
            bool gpu = nvidia || metal;
            // Approximate headroom only: do not label this as a guaranteed fit or speed measurement.
            long working = model.Bytes + Math.Max(GiB, model.Bytes / 4);
            bool fits = gpu ? (nvidia ? freeGpu >= (id == "gptoss20b-mxfp4" ? 11L * GiB : working) : freeRam >= working + 2 * GiB)
                            : freeRam >= working + 2 * GiB;
            bool recommended = fits && (gpu ? id != "4b-q4" : id == "4b-q4");
            int cpuMoe = nvidia && id == "gptoss20b-mxfp4" ? 2 : 0;
            string reason = id == "gptoss20b-mxfp4" ? "Core · marge GPU serrée, raisonnement et latence à mesurer"
                : id == "8b-q4" ? "Fast · privilégier GPU/Metal pour la réactivité" : "Profil léger · premier candidat CPU";
            if (!fits) reason += " · mémoire libre insuffisante ou inconnue : choix manuel à tester";
            if (cpuMoe > 0) reason += " · 2 couches d'experts sur CPU + Flash Attention";
            if (gpu && !nvidia) reason += " · mémoire unifiée estimée";
            result.Add(new(model, id == "gptoss20b-mxfp4" ? "core" : "fast", gpu, recommended, reason, cpuMoe));
        }
        return result.OrderByDescending(s => s.Recommended).ThenBy(s => s.Model.Bytes).ToArray();
    }
    private static async Task<string> CaptureAsync(string executable, string[] arguments, CancellationToken token)
    {
        ProcessStartInfo start = new(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (string arg in arguments) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Probe process failed.");
        Task<string> output = process.StandardOutput.ReadToEndAsync(token), error = process.StandardError.ReadToEndAsync(token);
        try
        {
            await process.WaitForExitAsync(token).WaitAsync(TimeSpan.FromSeconds(3), token);
            if (process.ExitCode != 0) throw new IOException("Hardware probe unavailable.");
            await error; return await output;
        }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(CancellationToken.None); } }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint Length, Load;
        public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
}
