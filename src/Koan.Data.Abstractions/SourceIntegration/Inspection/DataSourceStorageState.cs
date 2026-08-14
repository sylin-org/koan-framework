namespace Koan.Data.Abstractions;

/// <summary>Redacted source-storage status; detail codes must never contain connection material.</summary>
public sealed record DataSourceStorageState(DataSourceStorageStatus Status, string DetailCode);
