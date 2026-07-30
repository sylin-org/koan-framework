using Koan.Data.Abstractions.Sources;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.SqlServer.Runtime;

internal sealed class SqlServerRepositoryOptions
{
    public required string ConnectionString { get; init; }
    public string Source { get; init; } = Infrastructure.Constants.DefaultSource;
    public string Schema { get; init; } = Infrastructure.Constants.DefaultSchema;
    public RelationalDdlPolicy DdlPolicy { get; init; } = RelationalDdlPolicy.AutoCreate;
    public RelationalSchemaMatchingMode SchemaMatching { get; init; } = RelationalSchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; init; }
    public DataSourcePlan SourcePlan { get; init; } = DataSourcePlan.Default;
}
