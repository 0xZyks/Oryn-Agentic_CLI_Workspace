using System.Text.Json;

namespace Oryn.App;

public sealed record WorkspaceSettings
{
    public bool UseColor { get; init; } = true;
    public bool UseUnicode { get; init; } = true;
    public bool ShowHiddenFiles { get; init; }
    public int MaxDirectoryEntries { get; init; } = 1000;
    public int MaxPreviewCharacters { get; init; } = 65536;
    public int? RenderFramesPerSecond { get; init; }

    public static WorkspaceSettings Load(string? path)
    {
        if (path is null) return new();
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Configuration exceeds 64 KiB.");
        WorkspaceSettings settings = JsonSerializer.Deserialize<WorkspaceSettings>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true, UnmappedMemberHandling =
                System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("Configuration must be a JSON object.");
        if (settings.MaxDirectoryEntries is < 1 or > 10000 || settings.MaxPreviewCharacters is < 256 or > 1048576)
            throw new InvalidDataException("Invalid directory or preview limit in configuration.");
        RenderCadence.Validate(settings.RenderFramesPerSecond);
        return settings;
    }
}
