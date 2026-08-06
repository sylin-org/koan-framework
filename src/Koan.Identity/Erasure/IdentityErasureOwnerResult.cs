namespace Koan.Identity.Erasure;

/// <summary>Privacy-safe outcome reported by one semantic owner.</summary>
public sealed class IdentityErasureOwnerResult
{
    /// <summary>Stable semantic owner name.</summary>
    public string Owner { get; init; } = "";

    /// <summary>Execution order used for this attempt.</summary>
    public int Order { get; init; }

    /// <summary>True only when the owner reached its declared disposition.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Non-identifying disposition counts keyed by owner-defined stable names.</summary>
    public Dictionary<string, int> Counts { get; init; } = new(StringComparer.Ordinal);

    /// <summary>Short non-identifying outcome description.</summary>
    public string Summary { get; init; } = "";

    /// <summary>Privacy-safe retry or configuration guidance when unsuccessful.</summary>
    public string? Correction { get; init; }
}
