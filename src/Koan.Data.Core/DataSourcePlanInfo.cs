using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Core;

/// <summary>Redacted diagnostic projection of the immutable source plan used by execution.</summary>
public sealed record DataSourcePlanInfo(
    string Source,
    string Adapter,
    StorageLifecycle StorageLifecycle,
    DataSourceAccess Access,
    string RouteIdentity,
    IReadOnlyList<string> ReadLanes,
    IReadOnlyList<string> ClaimReferences,
    IReadOnlyList<string> Capabilities);
