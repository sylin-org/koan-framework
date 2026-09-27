namespace Koan.Data.Abstractions;

/// <summary>One provider-owned setup input, keyed exactly as a Data source setting.</summary>
public sealed record DataProviderSetupField(
    string Key,
    string Label,
    DataProviderSetupFieldKind Kind,
    bool Required = true,
    string? DefaultValue = null,
    string? Placeholder = null);
