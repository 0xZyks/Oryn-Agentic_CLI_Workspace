namespace Oryn.App;

public sealed record ModelProfile
{
    public string? CatalogId { get; init; }
    public string? ModelPath { get; init; }
    public int ContextTokens { get; init; } = 4096;
    public int MaxOutputTokens { get; init; } = 1024;
    public int SafetyTokens { get; init; } = 128;
    public int GpuLayers { get; init; } = 99;
    public int CpuMoeLayers { get; init; }
    public string FlashAttention { get; init; } = "auto";
    public bool Reasoning { get; init; }
    public int ReasoningBudget { get; init; } = 256;
    public string ReasoningEffort { get; init; } = "low";
    public double Temperature { get; init; } = 0.7;
    public double TopP { get; init; } = 0.95;
    public int TopK { get; init; } = 40;
    public double PresencePenalty { get; init; }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CatalogId) && string.IsNullOrWhiteSpace(ModelPath))
            throw new InvalidDataException("A model profile needs CatalogId or ModelPath.");
        if (ContextTokens is < 512 or > 32768 || MaxOutputTokens is < 32 or > 8192
            || SafetyTokens < 32 || MaxOutputTokens + SafetyTokens >= ContextTokens
            || GpuLayers is < 0 or > 999 || CpuMoeLayers is < 0 or > 256
            || FlashAttention is not ("on" or "off" or "auto") || !double.IsFinite(Temperature) || Temperature is < 0 or > 2
            || !double.IsFinite(TopP) || TopP is <= 0 or > 1 || TopK is < 0 or > 1000
            || !double.IsFinite(PresencePenalty) || PresencePenalty is < -2 or > 2
            || ReasoningBudget is < -1 or > 8192 || ReasoningEffort is not ("low" or "medium" or "high"))
            throw new InvalidDataException("Invalid model context, output, reasoning or sampling budget.");
    }
}
