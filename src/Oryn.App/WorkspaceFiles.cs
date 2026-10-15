using System.Text;

namespace Oryn.App;

public sealed record FileEntry(string Path, string Name, bool IsDirectory, bool IsLink);
public sealed record DirectoryResult(string Path, IReadOnlyList<FileEntry> Entries, bool Truncated, string? Error, string? Warning = null);
public sealed record FilePreview(string Path, string Content, bool Truncated, string? Error);

public interface IWorkspaceFiles
{
    Task<DirectoryResult> ListAsync(string path, CancellationToken cancellationToken);
    Task<FilePreview> ReadAsync(string path, CancellationToken cancellationToken);
    Task<WorkspaceSearchResult> SearchAsync(string query, CancellationToken cancellationToken) =>
        Task.FromResult(new WorkspaceSearchResult([], 0, false, "Search is not supported by this file service."));
}

public sealed partial class LocalWorkspaceFiles(string root, WorkspaceSettings settings) : IWorkspaceFiles
{
    private readonly string _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));

    public Task<DirectoryResult> ListAsync(string path, CancellationToken cancellationToken) => Task.Run(() =>
    {
        try
        {
            EnsureWithinRoot(path);
            List<FileEntry> entries = [];
            bool truncated = false;
            int unavailable = 0;
            foreach (string item in Directory.EnumerateFileSystemEntries(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string name = Path.GetFileName(item);
                if (!settings.ShowHiddenFiles && name.StartsWith('.')) continue;
                FileAttributes attributes;
                try { attributes = File.GetAttributes(item); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { unavailable++; continue; }
                if (!settings.ShowHiddenFiles && (name.StartsWith('.') || attributes.HasFlag(FileAttributes.Hidden))) continue;
                if (entries.Count == settings.MaxDirectoryEntries) { truncated = true; break; }
                entries.Add(new(item, name, attributes.HasFlag(FileAttributes.Directory), attributes.HasFlag(FileAttributes.ReparsePoint)));
            }
            FileEntry[] sorted = entries.OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            return new DirectoryResult(path, sorted, truncated, null,
                unavailable == 0 ? null : $"{unavailable} inaccessible entries skipped.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DirectoryResult(path, [], false, ex.Message);
        }
    }, cancellationToken);

    public Task<FilePreview> ReadAsync(string path, CancellationToken cancellationToken) =>
        Task.Run(() => ReadCoreAsync(path, cancellationToken), cancellationToken);

    private async Task<FilePreview> ReadCoreAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            EnsureWithinRoot(path);
            cancellationToken.ThrowIfCancellationRequested();
            // Empty regular files and zero-length special files need no stream opening.
            if (new FileInfo(path).Length == 0) return new(path, "", false, null);
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite,
                4096, FileOptions.Asynchronous);
            if (!stream.CanSeek) return new(path, "", false, "Special files cannot be previewed.");
            using StreamReader reader = new(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            char[] buffer = new char[settings.MaxPreviewCharacters + 1];
            int length = 0;
            while (length < buffer.Length)
            {
                int count = await reader.ReadAsync(buffer.AsMemory(length), cancellationToken);
                if (count == 0) break;
                length += count;
            }
            if (buffer.AsSpan(0, length).Contains('\0')) return new(path, "", false, "Binary file: preview unavailable.");
            bool truncated = length > settings.MaxPreviewCharacters;
            return new(path, TerminalText.Sanitize(new string(buffer, 0, Math.Min(length, settings.MaxPreviewCharacters))), truncated, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new(path, "", false, ex.Message);
        }
    }

    private void EnsureWithinRoot(string path)
    {
        string full = Path.GetFullPath(path);
        string relative = Path.GetRelativePath(_root, full);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            throw new UnauthorizedAccessException("Path is outside the project root.");
        // Disallow links in any descendant component; they may point outside the project or form cycles.
        string current = _root;
        foreach (string part in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".") continue;
            current = Path.Combine(current, part);
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new UnauthorizedAccessException("Symbolic links are visible but are not followed in M0.");
        }
    }
}

public static class TerminalText
{
    public static string Sanitize(string text) => string.Concat(text.Select(c =>
        c == '\t' ? "    " : c == '\r' || c == '\n' || !char.IsControl(c) ? c.ToString() : "�"));
}
