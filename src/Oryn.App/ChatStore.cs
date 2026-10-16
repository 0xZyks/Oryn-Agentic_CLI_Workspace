using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace Oryn.App;

public sealed record ChatSegment(string File, long First, long Last, int Count, int Bytes, string Sha256);
public sealed record ChatManifest(int Version, Guid ProjectId, long ArchivedThrough, List<ChatSegment> Segments);
public sealed record ChatJournalBatch(int Version, IReadOnlyList<ChatPreviewMessage> Messages);
public sealed record ProjectLocator(int Version, Guid ProjectId, string DataRoot);

/// <summary>Single-writer journal, immutable verified gzip segments and lazy history paging.</summary>
public sealed class ProjectChatStore : IDisposable
{
    private readonly object _sync = new();
    private readonly FileStream _lease;
    private readonly string _directory;
    private readonly int _segmentBytes, _recent;
    private readonly SortedDictionary<long, ChatPreviewMessage> _active = [];
    private ChatManifest _manifest;
    private bool _faulted;
    public Guid ProjectId => _manifest.ProjectId;
    public string DataDirectory => _directory;
    private string Journal => Path.Combine(_directory, "active.jsonl");
    private string ManifestPath => Path.Combine(_directory, "manifest.json");

    public ProjectChatStore(string projectRoot, WorkspaceSettings? settings = null)
    {
        settings ??= new(); _segmentBytes = settings.ChatSegmentBytes; _recent = settings.ChatRecentMessages;
        string locatorPath = Path.Combine(projectRoot, ".oryn", "project.json");
        ProjectLocator locator;
        if (File.Exists(locatorPath))
        {
            locator = JsonSerializer.Deserialize<ProjectLocator>(ReadBounded(locatorPath, 65536))
                ?? throw new InvalidDataException("Project locator is empty.");
            if (locator.Version != 1 || locator.ProjectId == Guid.Empty || string.IsNullOrWhiteSpace(locator.DataRoot))
                throw new InvalidDataException("Invalid project locator; original preserved.");
            if (Path.GetFullPath(locator.DataRoot, projectRoot) != Path.GetFullPath(settings.ChatDataRoot, projectRoot))
                throw new InvalidDataException("ChatDataRoot changed. Move/import existing data explicitly before changing its locator.");
        }
        else
        {
            locator = new(1, Guid.NewGuid(), settings.ChatDataRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(locatorPath)!);
            using FileStream created = new(locatorPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created.Write(JsonSerializer.SerializeToUtf8Bytes(locator)); created.Flush(true);
        }
        _directory = Path.Combine(Path.GetFullPath(locator.DataRoot, projectRoot), locator.ProjectId.ToString("D"));
        Directory.CreateDirectory(_directory);
        _lease = new(Path.Combine(_directory, ".writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        _manifest = new(1, locator.ProjectId, 0, []);
        try
        {
            if (File.Exists(ManifestPath))
                _manifest = JsonSerializer.Deserialize<ChatManifest>(ReadBounded(ManifestPath, 8 * 1024 * 1024))
                    ?? throw new InvalidDataException("Invalid chat manifest.");
            if (_manifest.Version != 1 || _manifest.ProjectId != locator.ProjectId || _manifest.Segments is null)
                throw new InvalidDataException("Unsupported or mismatched manifest; original preserved.");
            long last = 0;
            foreach (var segment in _manifest.Segments)
            {
                if (segment.First <= last || segment.Last < segment.First || segment.Bytes is < 1 or > 8 * 1024 * 1024
                    || !System.Text.RegularExpressions.Regex.IsMatch(segment.File, @"^segment-[0-9]+-[0-9]+\.json\.gz$"))
                    throw new InvalidDataException("Invalid segment catalog; original preserved.");
                last = segment.Last;
            }
            if (last != _manifest.ArchivedThrough) throw new InvalidDataException("Invalid archive sequence.");
            ReadJournal();
            string legacy = Path.Combine(projectRoot, ".oryn", "global-chat.json");
            if (!File.Exists(ManifestPath) && _active.Count == 0 && File.Exists(legacy))
            {
                var messages = JsonSerializer.Deserialize<List<ChatPreviewMessage>>(ReadBounded(legacy, 8 * 1024 * 1024))
                    ?? throw new InvalidDataException("Legacy chat invalid; original preserved.");
                long sequence = 0;
                foreach (var message in messages) { ValidateMessage(message); _active[++sequence] = message with { Sequence = sequence }; }
                RewriteJournal(); // Legacy file remains untouched after migration.
            }
            foreach (var (sequence, message) in _active.ToArray())
                if (message.Status == ChatMessageStatus.Generating) _active[sequence] = message with { Status = ChatMessageStatus.Interrupted };
            RewriteJournal(); PublishManifest(); Rotate();
        }
        catch { _lease.Dispose(); throw; }
    }

    public IReadOnlyList<ChatPreviewMessage> Load() => LoadOlder(long.MaxValue, _recent);
    public IReadOnlyList<ChatPreviewMessage> LoadOlder(long beforeSequence, int count = 40)
    {
        if (count is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(count));
        lock (_sync)
        {
            try
            {
                List<ChatPreviewMessage> result = _active.Values.Where(m => m.Sequence < beforeSequence).TakeLast(count).ToList();
                for (int i = _manifest.Segments.Count - 1; i >= 0 && result.Count < count; i--)
                {
                    var segment = _manifest.Segments[i];
                    if (segment.First >= beforeSequence) continue;
                    result.InsertRange(0, ReadSegment(segment).Where(m => m.Sequence < beforeSequence).TakeLast(count - result.Count));
                }
                return result.OrderBy(m => m.Sequence).ToArray();
            }
            catch { _faulted = true; throw; }
        }
    }
    public void Save(IEnumerable<ChatPreviewMessage> messages)
    {
        lock (_sync)
        {
            if (_faulted) throw new IOException("History is read-only after a storage error; original files preserved.");
            try
            {
                List<ChatPreviewMessage> changed = [];
                foreach (var message in messages)
                {
                    ValidateMessage(message);
                    if (message.Sequence <= 0) throw new InvalidDataException("Invalid message sequence.");
                    if (message.Sequence <= _manifest.ArchivedThrough) continue;
                    if (_active.TryGetValue(message.Sequence, out var previous))
                    {
                        if (previous.Id != message.Id) throw new InvalidDataException("Chat sequence collision.");
                        if (previous == message) continue;
                    }
                    changed.Add(message);
                }
                if (changed.Count > 0)
                {
                    using FileStream journal = new(Journal, FileMode.Append, FileAccess.Write, FileShare.Read);
                    journal.Write(JsonSerializer.SerializeToUtf8Bytes(new ChatJournalBatch(1, changed))); journal.WriteByte(10);
                    journal.Flush(true); // A user/assistant start is one recoverable journal transaction.
                    foreach (var message in changed) _active[message.Sequence] = message;
                }
                Rotate();
                if (new FileInfo(Journal).Length > _segmentBytes * 4L) RewriteJournal();
            }
            catch { _faulted = true; throw; }
        }
    }
    private void ReadJournal()
    {
        if (!File.Exists(Journal)) return;
        byte[] bytes = ReadBounded(Journal, 64 * 1024 * 1024);
        int start = 0;
        while (start < bytes.Length)
        {
            int end = Array.IndexOf(bytes, (byte)10, start);
            if (end < 0)
            {
                File.Copy(Journal, Journal + ".recovery-" + Guid.NewGuid().ToString("N"));
                break; // Preserve an incomplete tail before compaction; interior corruption is fatal.
            }
            using var record = JsonDocument.Parse(bytes.AsMemory(start, end - start));
            var batch = record.RootElement.TryGetProperty("Version", out _)
                ? JsonSerializer.Deserialize<ChatJournalBatch>(record.RootElement) ?? throw new InvalidDataException("Invalid journal transaction.")
                : new ChatJournalBatch(1, [JsonSerializer.Deserialize<ChatPreviewMessage>(record.RootElement) ?? throw new InvalidDataException("Invalid journal message.")]);
            if (batch.Version != 1 || batch.Messages is null) throw new InvalidDataException("Unknown journal format.");
            foreach (var message in batch.Messages)
            {
                ValidateMessage(message);
                if (message.Sequence <= 0) throw new InvalidDataException("Invalid chat sequence.");
                if (message.Sequence > _manifest.ArchivedThrough)
                {
                    if (_active.TryGetValue(message.Sequence, out var old) && old.Id != message.Id)
                        throw new InvalidDataException("Conflicting journal identity.");
                    _active[message.Sequence] = message;
                }
            }
            start = end + 1;
        }
    }
    private void Rotate()
    {
        while (_active.Count > _recent || JsonSerializer.SerializeToUtf8Bytes(_active.Values).Length > _segmentBytes)
        {
            int target = _active.Count > _recent ? _active.Count - _recent : _active.Count - 2;
            var batch = _active.Values.Take(Math.Max(0, target)).TakeWhile(m => m.Status != ChatMessageStatus.Generating).ToList();
            if (batch.Count == 0) break;
            while (batch.Count > 1 && JsonSerializer.SerializeToUtf8Bytes(batch).Length > _segmentBytes) batch.RemoveAt(batch.Count - 1);
            byte[] raw = JsonSerializer.SerializeToUtf8Bytes(batch);
            string name = $"segment-{batch[0].Sequence}-{batch[^1].Sequence}.json.gz";
            using MemoryStream compressed = new();
            using (GZipStream gzip = new(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) gzip.Write(raw);
            var segment = new ChatSegment(name, batch[0].Sequence, batch[^1].Sequence, batch.Count, raw.Length, Convert.ToHexString(SHA256.HashData(raw)));
            string path = Path.Combine(_directory, name);
            if (!File.Exists(path)) AtomicFile.Write(path, compressed.ToArray());
            _ = ReadSegment(segment); // Verify even an unpublished archive left by a crash, before publishing.
            _manifest = _manifest with { ArchivedThrough = segment.Last, Segments = [.. _manifest.Segments, segment] };
            PublishManifest();
            foreach (var message in batch) _active.Remove(message.Sequence);
            RewriteJournal(); // ArchivedThrough filters duplicates if a crash occurs before this rewrite.
        }
    }
    private IReadOnlyList<ChatPreviewMessage> ReadSegment(ChatSegment segment)
    {
        using FileStream file = File.OpenRead(Path.Combine(_directory, segment.File));
        using GZipStream gzip = new(file, CompressionMode.Decompress);
        using MemoryStream raw = new();
        byte[] buffer = new byte[8192]; int n;
        while ((n = gzip.Read(buffer)) > 0)
        {
            if (raw.Length + n > segment.Bytes) throw new InvalidDataException("Archive exceeds declared size.");
            raw.Write(buffer, 0, n);
        }
        byte[] bytes = raw.ToArray();
        if (bytes.Length != segment.Bytes || Convert.ToHexString(SHA256.HashData(bytes)) != segment.Sha256)
            throw new InvalidDataException("Archive integrity check failed; original preserved.");
        var messages = JsonSerializer.Deserialize<List<ChatPreviewMessage>>(bytes) ?? throw new InvalidDataException("Invalid archive.");
        if (messages.Count != segment.Count || messages.Count == 0 || messages[0].Sequence != segment.First || messages[^1].Sequence != segment.Last)
            throw new InvalidDataException("Archive catalog mismatch.");
        foreach (var message in messages) ValidateMessage(message);
        return messages;
    }
    private void RewriteJournal()
    {
        using MemoryStream output = new();
        output.Write(JsonSerializer.SerializeToUtf8Bytes(new ChatJournalBatch(1, _active.Values.ToArray()))); output.WriteByte(10);
        AtomicFile.Write(Journal, output.ToArray());
    }
    private void PublishManifest() => AtomicFile.Write(ManifestPath, JsonSerializer.SerializeToUtf8Bytes(_manifest));
    private static byte[] ReadBounded(string path, int limit)
    {
        if (new FileInfo(path).Length > limit) throw new InvalidDataException("Chat safety bound exceeded; original preserved.");
        return File.ReadAllBytes(path);
    }
    private static void ValidateMessage(ChatPreviewMessage message)
    {
        if (message.Id == Guid.Empty || message.Text is null || message.ContextSources is null || !Enum.IsDefined(message.Role) || !Enum.IsDefined(message.Status))
            throw new InvalidDataException("Invalid chat message; original preserved.");
    }
    public void Dispose() { lock (_sync) _lease.Dispose(); }
}
