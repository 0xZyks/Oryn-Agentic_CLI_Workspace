namespace Oryn.App;

public sealed record WorkspaceSearchMatch(FileEntry Entry, string RelativePath, int? Line, string? Snippet);
public sealed record WorkspaceSearchResult(IReadOnlyList<WorkspaceSearchMatch> Matches, int FilesScanned,
    bool Partial, string Summary);

public sealed partial class LocalWorkspaceFiles
{
    public Task<WorkspaceSearchResult> SearchAsync(string query, CancellationToken cancellationToken) => Task.Run(async () =>
    {
        query = query.Trim();
        if (query.Length == 0) return new WorkspaceSearchResult([], 0, false, "Type a file name or text.");
        if (query.Length > 256) return new WorkspaceSearchResult([], 0, true, "Search is limited to 256 characters.");
        List<WorkspaceSearchMatch> matches = [];
        Stack<string> pending = new(); pending.Push(_root);
        int filesScanned = 0, directories = 0, unavailable = 0;
        long charactersRead = 0;
        bool partial = false, limitReached = false;
        const int fileLimit = 5000, directoryLimit = 1000, resultLimit = 200;
        const long textBudget = 8 * 1024 * 1024;
        HashSet<string> excluded = new(StringComparer.OrdinalIgnoreCase) { ".git", ".local", "bin", "obj", "node_modules", "artifacts", "vendor" };
        HashSet<string> binaryExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".ico", ".pdf", ".zip", ".gz", ".7z", ".tar", ".dll", ".exe", ".pdb", ".so", ".dylib", ".a", ".o", ".woff", ".woff2", ".ttf", ".otf", ".mp3", ".mp4", ".wav", ".ogg", ".db", ".sqlite", ".sqlite3" };
        while (pending.Count > 0 && !limitReached)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++directories > directoryLimit) { partial = true; break; }
            DirectoryResult directory = await ListAsync(pending.Pop(), cancellationToken);
            partial |= directory.Truncated;
            if (directory.Error is not null) { unavailable++; continue; }
            if (directory.Warning is not null) unavailable++;
            foreach (FileEntry entry in directory.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsLink) continue;
                if (entry.IsDirectory) { if (!excluded.Contains(entry.Name)) pending.Push(entry.Path); continue; }
                if (filesScanned >= fileLimit || matches.Count >= resultLimit) { partial = limitReached = true; break; }
                filesScanned++;
                string relative = Path.GetRelativePath(_root, entry.Path);
                if (entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
                { matches.Add(new(entry, relative, null, null)); continue; }
                if (binaryExtensions.Contains(Path.GetExtension(entry.Name))) continue;
                if (charactersRead + settings.MaxPreviewCharacters > textBudget) { partial = true; continue; }
                FilePreview preview = await ReadAsync(entry.Path, cancellationToken);
                if (preview.Error is not null) { unavailable++; continue; }
                partial |= preview.Truncated;
                charactersRead += preview.Content.Length;
                int offset = preview.Content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
                if (offset < 0) continue;
                int line = 1;
                for (int i = 0; i < offset; i++) if (preview.Content[i] == '\n') line++;
                int start = preview.Content.LastIndexOf('\n', offset);
                start = start < 0 ? 0 : start + 1;
                int end = preview.Content.IndexOf('\n', offset);
                if (end < 0) end = preview.Content.Length;
                // Keep the matched area visible even on unusually long lines.
                int snippetStart = Math.Max(start, offset - 30);
                string snippet = preview.Content[snippetStart..Math.Min(end, snippetStart + 160)].Trim();
                matches.Add(new(entry, relative, line, snippet));
            }
        }
        partial |= unavailable > 0;
        string summary = $"{matches.Count} files / {filesScanned} scanned" + (partial ? " / partial" : "")
            + (unavailable > 0 ? $" / {unavailable} unreadable" : "");
        return new WorkspaceSearchResult(matches, filesScanned, partial, summary);
    }, cancellationToken);
}
