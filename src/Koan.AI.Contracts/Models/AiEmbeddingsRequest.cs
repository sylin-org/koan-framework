namespace Koan.AI.Contracts.Models;

public record AiEmbeddingsRequest
{
    public List<string> Input { get; init; } = new();
    public string? Model { get; init; }

    /// <summary>
    /// ADR-0015: Internal property set by router to inject member URL into adapter.
    /// Adapters use this to route to specific endpoints in singleton pattern.
    /// </summary>
    public string? InternalConnectionString { get; set; }
}