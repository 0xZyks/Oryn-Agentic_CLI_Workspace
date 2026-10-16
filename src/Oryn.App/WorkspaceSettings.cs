using System.Text.Json;

namespace Oryn.App;

public sealed record WorkspaceSettings
{
    public const string ProjectFileName = "oryn.settings.json";
    public bool UseColor { get; init; } = true;
    public bool UseUnicode { get; init; } = true;
    public bool ShowHiddenFiles { get; init; }
    public int MaxDirectoryEntries { get; init; } = 1000;
    public int MaxPreviewCharacters { get; init; } = 65536;
    public int? RenderFramesPerSecond { get; init; }
    public string? InferenceModelPath { get; init; }
    public string? InferenceRunnerPath { get; init; }
    public string? PythonExecutable { get; init; }
    public string InferenceEndpoint { get; init; } = "http://127.0.0.1:18080";
    public string? LlamaServerPath { get; init; }
    public string? InferenceAssetsRoot { get; init; }
    public string SelectedModelProfile { get; init; } = "core";
    public Dictionary<string, ModelProfile> ModelProfiles { get; init; } = [];
    public int StartupTimeoutSeconds { get; init; } = 120;
    public int GenerationTimeoutSeconds { get; init; } = 180;
    public string ChatDataRoot { get; init; } = ".local/oryn/projects";
    public int ChatSegmentBytes { get; init; } = 262144;
    public int ChatRecentMessages { get; init; } = 40;
    public bool InferenceSetupCompleted { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public ModelProfile? SelectedProfile => ModelProfiles.GetValueOrDefault(SelectedModelProfile)
        ?? (string.IsNullOrWhiteSpace(InferenceModelPath) ? null : new ModelProfile {
            ModelPath = InferenceModelPath, Reasoning = Path.GetFileName(InferenceModelPath).StartsWith("gpt-oss", StringComparison.OrdinalIgnoreCase),
            Temperature = Path.GetFileName(InferenceModelPath).StartsWith("gpt-oss", StringComparison.OrdinalIgnoreCase) ? 1 : 0.7,
            TopP = Path.GetFileName(InferenceModelPath).StartsWith("gpt-oss", StringComparison.OrdinalIgnoreCase) ? 1 : 0.8,
            TopK = Path.GetFileName(InferenceModelPath).StartsWith("gpt-oss", StringComparison.OrdinalIgnoreCase) ? 0 : 20,
            PresencePenalty = Path.GetFileName(InferenceModelPath).StartsWith("Qwen3-8B", StringComparison.OrdinalIgnoreCase) ? 1.5 : 0 });

    public static string? ResolvePath(string projectRoot, string? explicitPath)
    {
        if (explicitPath is not null) return explicitPath;
        string projectSettings = Path.Combine(projectRoot, ProjectFileName);
        return File.Exists(projectSettings) ? projectSettings : null;
    }

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
        settings.Validate();
        return settings;
    }

    public void Validate()
    {
        if (ModelProfiles is null || ModelProfiles.Count > 16 || ModelProfiles.Any(p => string.IsNullOrWhiteSpace(p.Key) || p.Value is null)
            || string.IsNullOrWhiteSpace(SelectedModelProfile)) throw new InvalidDataException("Invalid model profile registry.");
        RenderCadence.Validate(RenderFramesPerSecond);
        if (StartupTimeoutSeconds is < 5 or > 600 || GenerationTimeoutSeconds is < 5 or > 3600
            || ChatSegmentBytes is < 4096 or > 4194304 || ChatRecentMessages is < 4 or > 200 || string.IsNullOrWhiteSpace(ChatDataRoot))
            throw new InvalidDataException("Invalid inference timeout or chat storage limits.");
        foreach (var profile in ModelProfiles.Values) profile.Validate();
        if (ModelProfiles.Count > 0 && !ModelProfiles.ContainsKey(SelectedModelProfile))
            throw new InvalidDataException("SelectedModelProfile does not exist in ModelProfiles.");
        if (!Uri.TryCreate(InferenceEndpoint, UriKind.Absolute, out Uri? endpoint)
            || endpoint.Scheme != "http" || endpoint.Host != "127.0.0.1" || endpoint.Port is < 1024 or > 65535
            || endpoint.AbsolutePath != "/" || endpoint.Query.Length > 0 || endpoint.UserInfo.Length > 0)
            throw new InvalidDataException("InferenceEndpoint must be http://127.0.0.1:PORT with no path or credentials.");
    }

    public static void Save(string path, WorkspaceSettings settings)
    {
        settings.Validate();
        AtomicFile.Write(path, System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(settings,
            new JsonSerializerOptions { WriteIndented = true })));
    }
}
