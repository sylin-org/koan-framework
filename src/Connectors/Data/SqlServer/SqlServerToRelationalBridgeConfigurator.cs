using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Infrastructure;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Connector.SqlServer;

// Bridge SQL Server adapter options into the global RelationalMaterializationOptions used by orchestrator.
// Carried from the former manual SqlServerRegistration so the auto-discovery path is complete.
internal sealed class SqlServerToRelationalBridgeConfigurator(IOptions<SqlServerOptions> sqlOpts, IConfiguration cfg) : IConfigureOptions<RelationalMaterializationOptions>
{
    public void Configure(RelationalMaterializationOptions options)
    {
        var so = sqlOpts.Value;
        // Map DDL policy
        options.DdlPolicy = so.DdlPolicy switch
        {
            SchemaDdlPolicy.NoDdl => RelationalDdlPolicy.NoDdl,
            SchemaDdlPolicy.Validate => RelationalDdlPolicy.Validate,
            SchemaDdlPolicy.AutoCreate => RelationalDdlPolicy.AutoCreate,
            _ => options.DdlPolicy
        };
        // Use computed projections by default for SQL Server (supports JSON_VALUE)
        options.Materialization = RelationalMaterializationPolicy.ComputedProjections;
        // Map matching mode
        options.SchemaMatching = so.SchemaMatching == SchemaMatchingMode.Strict ? RelationalSchemaMatchingMode.Strict : RelationalSchemaMatchingMode.Relaxed;
        // Allow production DDL only when explicitly allowed or when provider option permits
        var allowMagic = Configuration.Read(cfg, Constants.Configuration.Koan.AllowMagicInProduction, false);
        options.AllowProductionDdl = so.AllowProductionDdl || allowMagic;
    }
}
