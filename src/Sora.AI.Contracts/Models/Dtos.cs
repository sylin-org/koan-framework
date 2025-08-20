using System.Collections.Generic;

namespace Sora.AI.Contracts.Models;

public record AiMessage(string Role, string Content);

public record AiPromptOptions
{
    public double? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    public double? TopP { get; init; }
    public string[]? Stop { get; init; }
    public int? Seed { get; init; }
    public string? Profile { get; init; }
}

public record AiRouteHints
{
    public string? AdapterId { get; init; }
    public string? Policy { get; init; }
    public string? StickyKey { get; init; }
}

public record AiChatRequest
{
    public List<AiMessage> Messages { get; init; } = new();
    public string? Model { get; init; }
    public AiPromptOptions? Options { get; init; }
    public AiRouteHints? Route { get; init; }
}

public record AiChatResponse
{
    public string Text { get; init; } = string.Empty;
    public string? FinishReason { get; init; }
    public int? TokensIn { get; init; }
    public int? TokensOut { get; init; }
    public string? Model { get; init; }
    public string? AdapterId { get; init; }
}

public record AiChatChunk
{
    public string? DeltaText { get; init; }
    public int Index { get; init; }
    public string? Model { get; init; }
    public string? AdapterId { get; init; }
    public int? TokensOutInc { get; init; }
}

public record AiEmbeddingsRequest
{
    public List<string> Input { get; init; } = new();
    public string? Model { get; init; }
}

public record AiEmbeddingsResponse
{
    public List<float[]> Vectors { get; init; } = new();
    public string? Model { get; init; }
    public int? Dimension { get; init; }
}
