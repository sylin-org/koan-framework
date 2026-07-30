using Koan.Data.Abstractions.Sources;

namespace Koan.Data.Connector.Sqlite.Runtime;

internal sealed record SqliteRoute(
    string Source,
    string ConnectionString,
    SqliteOptions Options,
    DataSourcePlan Policy,
    IReadOnlyDictionary<string, string> ReadLanes);
