namespace Koan.AI.Contracts.Shared;

/// <summary>
/// Lightweight dataset identity with content hash for lineage tracking.
/// Shared across Dataset, Training, and Eval contexts.
/// </summary>
public sealed record DatasetRef(string Id, string? Hash = null)
{
    public override string ToString() =>
        Hash is not null ? $"{Id} ({Hash[..8]}...)" : Id;
}
