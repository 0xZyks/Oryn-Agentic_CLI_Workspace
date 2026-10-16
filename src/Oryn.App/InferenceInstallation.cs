using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Oryn.App;

public sealed record CatalogModel(string Id, string Repository, string Revision, string FileName, long Bytes, string Sha256, string License);
public sealed record RuntimeArchive(string Name, long Bytes, string Sha256);
public static class InferenceInstallation
{
    private static readonly JsonDocument Manifest = JsonDocument.Parse(Assembly.GetExecutingAssembly().GetManifestResourceStream("Oryn.InferenceManifest.json")!);
    public static string Version => Manifest.RootElement.GetProperty("backend").GetProperty("version").GetString()!;
    public static string? DevelopmentRoot
    {
        get
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "scripts/inference/manifest.json"))) return directory.FullName;
            return null;
        }
    }
    public static string AssetsRoot(string root, WorkspaceSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.InferenceAssetsRoot)) return Path.GetFullPath(settings.InferenceAssetsRoot, root);
        if (DevelopmentRoot is { } development && Directory.Exists(Path.Combine(development, ".local/inference")))
            return Path.Combine(development, ".local/inference");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "oryn", "inference");
    }
    public static IReadOnlyList<CatalogModel> Models
    {
        get
        {
            List<CatalogModel> result = [ParseModel("8b-q4", Manifest.RootElement.GetProperty("model"))];
            result.AddRange(Manifest.RootElement.GetProperty("candidates").EnumerateObject().Select(p => ParseModel(p.Name, p.Value)));
            return result;
        }
    }
    private static CatalogModel ParseModel(string id, JsonElement item) => new(id,
        item.GetProperty("repository").GetString()!, item.GetProperty("revision").GetString()!,
        item.GetProperty("filename").GetString()!, item.GetProperty("bytes").GetInt64(),
        item.GetProperty("sha256").GetString()!, item.GetProperty("license").GetString()!);
    public static string ModelPath(string root, WorkspaceSettings settings, ModelProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.ModelPath)) return Path.GetFullPath(profile.ModelPath, root);
        var model = Models.SingleOrDefault(m => m.Id == profile.CatalogId) ?? throw new IOException("Unknown model CatalogId.");
        return Path.Combine(AssetsRoot(root, settings), "models", model.FileName);
    }
    public static string? RuntimeKey(bool gpu)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64 && OperatingSystem.IsLinux()) return gpu ? "linux-x64-cuda" : "linux-x64-cpu";
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64 && OperatingSystem.IsWindows()) return "win-x64-cpu";
        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 && OperatingSystem.IsMacOS()) return "osx-arm64-metal";
        return null;
    }
    public static IReadOnlyList<RuntimeArchive> Archives(string key) => Manifest.RootElement.GetProperty("runtime_downloads").GetProperty(key)
        .EnumerateArray().Select(a => new RuntimeArchive(a.GetProperty("name").GetString()!, a.GetProperty("bytes").GetInt64(), a.GetProperty("sha256").GetString()!)).ToArray();
    public static string? FindServer(string root, WorkspaceSettings settings)
    {
        bool gpu = settings.SelectedProfile?.GpuLayers > 0;
        string? key = RuntimeKey(gpu);
        if (key is null) return null;
        string directory = Path.Combine(AssetsRoot(root, settings), "runtimes", key + "-" + Version);
        if (!File.Exists(Path.Combine(directory, ".installed.json"))) return null;
        return Directory.GetFiles(directory, OperatingSystem.IsWindows() ? "llama-server.exe" : "llama-server", SearchOption.AllDirectories).SingleOrDefault();
    }
    public static bool HasBackend(string root, WorkspaceSettings settings)
    {
        if (settings.LlamaServerPath is { } server) return File.Exists(Path.GetFullPath(server, root));
        if (settings.InferenceRunnerPath is { } runner) return File.Exists(Path.GetFullPath(runner, root));
        if (FindServer(root, settings) is not null) return true;
        return OperatingSystem.IsLinux() && DevelopmentRoot is { } development
            && Directory.Exists(Path.Combine(development, ".local/inference", "llama-" + Version));
    }
    public static string LibraryRoot(string server)
    {
        for (DirectoryInfo? current = new(Path.GetDirectoryName(server)!); current is not null; current = current.Parent)
            if (File.Exists(Path.Combine(current.FullName, ".installed.json"))) return current.FullName;
        return Path.GetDirectoryName(server)!;
    }

    public static async Task InstallAsync(string root, WorkspaceSettings settings, CatalogModel model, bool gpu,
        IProgress<string> progress, CancellationToken token)
    {
        bool needsBackend = !HasBackend(root, settings);
        string? key = RuntimeKey(gpu);
        if (needsBackend && key is null) throw new IOException("No pinned backend for this platform. Configure LlamaServerPath manually.");
        string assets = AssetsRoot(root, settings); Directory.CreateDirectory(assets);
        using FileStream lease = new(Path.Combine(assets, ".install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        using HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };
        string runtime = Path.Combine(assets, "runtimes", key + "-" + Version);
        if (needsBackend && !File.Exists(Path.Combine(runtime, ".installed.json")))
        {
            if (Directory.Exists(runtime)) throw new IOException("Incomplete runtime preserved; move it aside before retrying installation.");
            string staging = runtime + ".staging-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(staging);
            foreach (var archive in Archives(key!))
            {
                string source = Path.Combine(assets, "downloads", archive.Name);
                await DownloadAsync(http, new Uri($"https://github.com/ggml-org/llama.cpp/releases/download/{Version}/{archive.Name}"),
                    source, archive.Bytes, archive.Sha256, progress, token);
                progress.Report("Extraction " + archive.Name);
                await ExtractAsync(source, staging, token);
            }
            AtomicFile.Write(Path.Combine(staging, ".installed.json"), JsonSerializer.SerializeToUtf8Bytes(new { version = Version, platform = key }));
            Directory.CreateDirectory(Path.GetDirectoryName(runtime)!); Directory.Move(staging, runtime);
        }
        await DownloadAsync(http, new Uri($"https://huggingface.co/{model.Repository}/resolve/{model.Revision}/{model.FileName}"),
            Path.Combine(assets, "models", model.FileName), model.Bytes, model.Sha256, progress, token);
        progress.Report("Installation vérifiée. Vous pouvez lancer l'essai local.");
    }
    private static async Task DownloadAsync(HttpClient http, Uri uri, string destination, long size, string hash,
        IProgress<string> progress, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (File.Exists(destination))
        {
            await using var existing = File.OpenRead(destination);
            if (Convert.ToHexString(await SHA256.HashDataAsync(existing, token)).Equals(hash, StringComparison.OrdinalIgnoreCase)) return;
            throw new IOException("Existing file has a different hash and was preserved: " + destination);
        }
        string temporary = destination + ".part";
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync(token))
        await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
        {
            byte[] buffer = new byte[81920]; long total = 0, next = 0; int n;
            while ((n = await input.ReadAsync(buffer, token)) > 0)
            {
                total += n; if (total > size) throw new IOException("Download exceeds pinned size.");
                await file.WriteAsync(buffer.AsMemory(0, n), token);
                if (total >= next) { progress.Report($"{Path.GetFileName(destination)} : {100.0 * total / size:F0}%"); next = total + 16 * 1024 * 1024; }
            }
            if (total != size) throw new IOException("Incomplete download preserved as .part.");
            await file.FlushAsync(token);
        }
        await using (var downloaded = File.OpenRead(temporary))
            if (!Convert.ToHexString(await SHA256.HashDataAsync(downloaded, token)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new IOException("SHA256 mismatch; download was not installed.");
        File.Move(temporary, destination);
    }
    private static string SafePath(string root, string entry)
    {
        string full = Path.GetFullPath(entry, root);
        if (full != Path.GetFullPath(root) && !full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new IOException("Archive path escapes installation directory.");
        return full;
    }
    private static async Task ExtractAsync(string archive, string destination, CancellationToken token)
    {
        if (archive.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(archive);
            foreach (var entry in zip.Entries)
            {
                token.ThrowIfCancellationRequested();
                string target = SafePath(destination, entry.FullName);
                if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                await using var output = new FileStream(target, FileMode.Create, FileAccess.Write);
                await using var input = entry.Open(); await input.CopyToAsync(output, token);
            }
            return;
        }
        await using var compressed = File.OpenRead(archive);
        await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var tar = new TarReader(gzip);
        List<(string Path, string Target)> links = [];
        TarEntry? item;
        while ((item = await tar.GetNextEntryAsync(cancellationToken: token)) is not null)
        {
            string target = SafePath(destination, item.Name);
            if (item.EntryType == TarEntryType.Directory) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (item.EntryType == TarEntryType.SymbolicLink)
            {
                string resolved = SafePath(destination, Path.GetRelativePath(destination, Path.GetFullPath(item.LinkName, Path.GetDirectoryName(target)!)));
                links.Add((target, resolved)); continue;
            }
            if (item.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) throw new IOException("Unsupported archive entry.");
            await using (var output = new FileStream(target, FileMode.Create, FileAccess.Write))
                if (item.DataStream is not null) await item.DataStream.CopyToAsync(output, token);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(target, item.Mode);
        }
        foreach (var link in links) File.CreateSymbolicLink(link.Path, Path.GetRelativePath(Path.GetDirectoryName(link.Path)!, link.Target));
    }
}
