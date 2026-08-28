using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Connector.DuckDb.Runtime;

internal sealed record DuckDbRoute(
    string Source,
    string ConnectionString,
    DuckDbOptions Options,
    DataSourcePlan Policy,
    IReadOnlyDictionary<string, string> ReadLanes);
