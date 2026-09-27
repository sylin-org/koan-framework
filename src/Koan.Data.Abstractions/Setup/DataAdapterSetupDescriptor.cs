namespace Koan.Data.Abstractions;

/// <summary>Provider-owned display and input metadata for configuring one candidate source.</summary>
public sealed record DataAdapterSetupDescriptor(
    string DisplayName,
    IReadOnlyList<DataProviderSetupField> Fields);
