using System.Collections.Generic;

namespace Koan.AI.Contracts.Models;

/// <summary>
/// Carries optional contextual metadata that can influence routing, augmentations, or
/// downstream adapters (profiles, budgets, tenant identifiers, etc.).
/// </summary>
public sealed record AiConversationContext
{
    /// <summary>
    /// Named conversation profile (e.g., "support", "sales") that adapters may use to select
    /// a default model or prompt template.
    /// </summary>
    public string? Profile { get; init; }

    /// <summary>
    /// Optional budget identifier or cost center.
    /// </summary>
    public string? Budget { get; init; }

    /// <summary>
    /// Arbitrary key/value metadata propagated alongside the conversation.
    /// </summary>
    public IDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Optional references to external resources that augmentations may hydrate (e.g., document IDs).
    /// </summary>
    public IList<string>? GroundingReferences { get; init; }
}
