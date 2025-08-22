namespace Sora.AI.Contracts.Models;

public record AiCapabilities
{
    public string AdapterId { get; init; } = string.Empty;
    public string AdapterType { get; init; } = string.Empty;
    public string? Version { get; init; }
    public bool SupportsChat { get; init; }
    public bool SupportsStreaming { get; init; }
    public bool SupportsEmbeddings { get; init; }
}