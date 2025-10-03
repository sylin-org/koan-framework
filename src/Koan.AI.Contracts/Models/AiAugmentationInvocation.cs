using System.Collections.Generic;

namespace Koan.AI.Contracts.Models;

/// <summary>
/// Represents an augmentation (e.g., RAG, moderation) that should participate in the conversation pipeline.
/// </summary>
public sealed record AiAugmentationInvocation
{
    public required string Name { get; init; }
    public bool Enabled { get; init; } = true;
    public IDictionary<string, object?>? Parameters { get; init; }
}
