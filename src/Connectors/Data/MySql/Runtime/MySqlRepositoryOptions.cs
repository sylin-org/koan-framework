using Koan.Data.Abstractions.Sources;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.MySql.Runtime;

internal sealed class MySqlRepositoryOptions
{
    public required string ConnectionString { get; init; }
    public string Source { get; init; } = Infrastructure.Constants.DefaultSource;
    public string Database { get; init; } = Infrastructure.Constants.DefaultDatabase;
    public RelationalDdlPolicy DdlPolicy { get; init; } = RelationalDdlPolicy.AutoCreate;
    public RelationalSchemaMatchingMode SchemaMatching { get; init; } = RelationalSchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; init; }
    public DataSourcePlan SourcePlan { get; init; } = DataSourcePlan.Default;
}
