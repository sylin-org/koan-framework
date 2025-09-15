using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Infrastructure;
using Koan.Data.Relational.Orchestration;

namespace Koan.Data.Sqlite;

internal sealed class SqliteToRelationalBridgeConfigurator(IOptions<SqliteOptions> sqliteOptions, IConfiguration cfg) : IConfigureOptions<RelationalMaterializationOptions>
{
    public void Configure(RelationalMaterializationOptions options)
    {
        var so = sqliteOptions.Value;
        // Map DDL
        options.DdlPolicy = so.DdlPolicy switch
        {
            SchemaDdlPolicy.NoDdl => RelationalDdlPolicy.NoDdl,
            SchemaDdlPolicy.Validate => RelationalDdlPolicy.Validate,
            SchemaDdlPolicy.AutoCreate => RelationalDdlPolicy.AutoCreate,
            _ => options.DdlPolicy
        };
        // Map matching
        options.SchemaMatching = so.SchemaMatching == SchemaMatchingMode.Strict ? RelationalSchemaMatchingMode.Strict : RelationalSchemaMatchingMode.Relaxed;
        // Favor materialized projections for SQLite so projected columns are created and validated
        options.Materialization = RelationalMaterializationPolicy.PhysicalColumns;
        // Production guardrail: for SQLite, default to allowing DDL when AutoCreate is selected to favor local/test usability
        var magic = Configuration.Read(cfg, Constants.Configuration.Koan.AllowMagicInProduction, false);
        options.AllowProductionDdl = so.AllowProductionDdl || magic || options.DdlPolicy == RelationalDdlPolicy.AutoCreate;
        // When materialization policy is None, we still want projections checked if present; leave options.Materialization at default
        // Fail-on-mismatch follows orchestrator defaults; no explicit override here
    }
}