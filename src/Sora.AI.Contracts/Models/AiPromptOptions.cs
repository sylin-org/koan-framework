namespace Sora.AI.Contracts.Models;

public record AiPromptOptions
{
    public double? Temperature { get; init; }
    public int? MaxOutputTokens { get; init; }
    public double? TopP { get; init; }
    public string[]? Stop { get; init; }
    public int? Seed { get; init; }
    public string? Profile { get; init; }
}