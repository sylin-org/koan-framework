using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Abstractions;

/// <summary>Pure, redacted projection of the exact frozen decisions used by one source.</summary>
public sealed record DataSourceDescription(
    string Source,
    string Provider,
    string DecisionId,
    StorageLifecycle StorageLifecycle,
    DataSourceAccess Access,
    IReadOnlyList<string> ReadLanes,
    IReadOnlyList<string> Capabilities,
    IReadOnlyList<DataClaim> Claims,
    SourceIntegrationCapabilities Operations,
    SourceInspectionCapabilities Inspection,
    IReadOnlyList<DataOperationDescription> RegisteredOperations,
    IReadOnlyList<DataMappingDescription> Mappings);
