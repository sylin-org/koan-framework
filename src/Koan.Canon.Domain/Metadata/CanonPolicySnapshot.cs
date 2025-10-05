namespace Koan.Canon.Domain.Metadata;

/// <summary>
/// Represents the evaluated outcome for a canon policy.
/// </summary>
public sealed class CanonPolicySnapshot
{
    /// <summary>
    /// Policy identifier.
    /// </summary>
    public string Policy { get; set; } = string.Empty;

    /// <summary>
    /// Selected outcome or transformer.
    /// </summary>
    public string Outcome { get; set; } = string.Empty;

    /// <summary>
    /// Policy version, if applicable.
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Timestamp when the policy was applied.
    /// </summary>
    public DateTimeOffset AppliedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Additional evidence recorded by the policy.
    /// </summary>
    public Dictionary<string, string?> Evidence
    {
        get => _evidence;
        set => _evidence = value is null
            ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(value, StringComparer.OrdinalIgnoreCase);
    }

    private Dictionary<string, string?> _evidence = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Clones the snapshot to preserve immutability semantics.
    /// </summary>
    public CanonPolicySnapshot Clone()
    {
        return new CanonPolicySnapshot
        {
            Policy = Policy,
            Outcome = Outcome,
            Version = Version,
            AppliedAt = AppliedAt,
            Evidence = new Dictionary<string, string?>(_evidence, StringComparer.OrdinalIgnoreCase)
        };
    }
}
