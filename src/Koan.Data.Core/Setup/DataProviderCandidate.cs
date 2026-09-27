using Koan.Data.Abstractions;

namespace Koan.Data.Core;

/// <summary>One installed record provider available to an application setup experience.</summary>
public sealed record DataProviderCandidate(
    string Id,
    string DisplayName,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<DataProviderSetupField> Fields,
    bool SupportsProbe);
