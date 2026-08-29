using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Connector.Firebird.Runtime;

internal sealed record FirebirdRoute(
    string Source,
    string ConnectionString,
    FirebirdOptions Options,
    DataSourcePlan Policy,
    IReadOnlyDictionary<string, string> ReadLanes);
