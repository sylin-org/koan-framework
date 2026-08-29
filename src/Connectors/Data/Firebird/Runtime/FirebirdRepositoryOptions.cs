using Koan.Data.Abstractions.Sources;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.Firebird.Runtime;

internal sealed class FirebirdRepositoryOptions
{
    public required string ConnectionString { get; init; }
    public string Source { get; init; } = Infrastructure.Constants.DefaultSource;

    /// <summary>The database the connection opens; Firebird has one per connection, so it is the policy's schema leg.</summary>
    public string Database { get; init; } = Infrastructure.Constants.DefaultDatabase;
    public RelationalDdlPolicy DdlPolicy { get; init; } = RelationalDdlPolicy.AutoCreate;
    public RelationalSchemaMatchingMode SchemaMatching { get; init; } = RelationalSchemaMatchingMode.Relaxed;
    public bool AllowProductionDdl { get; init; }
    public DataSourcePlan SourcePlan { get; init; } = DataSourcePlan.Default;
}
