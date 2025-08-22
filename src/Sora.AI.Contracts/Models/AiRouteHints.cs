namespace Sora.AI.Contracts.Models;

public record AiRouteHints
{
    public string? AdapterId { get; init; }
    public string? Policy { get; init; }
    public string? StickyKey { get; init; }
}