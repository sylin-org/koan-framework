namespace Koan.Data.Abstractions;

/// <summary>One stable, redacted executable adapter claim.</summary>
public sealed record DataClaim(
    string Reference,
    string Profile,
    string Owner,
    string? Qualifier,
    string? Capability,
    bool Advertised);
