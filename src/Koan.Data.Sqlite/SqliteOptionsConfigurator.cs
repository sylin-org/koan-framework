using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Infrastructure;

namespace Koan.Data.Sqlite;

internal sealed class SqliteOptionsConfigurator(IConfiguration config) : IConfigureOptions<SqliteOptions>
{
    public void Configure(SqliteOptions options)
    {
        options.ConnectionString = Configuration.ReadFirst(
            config,
            defaultValue: options.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsSqlite,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);
        // Paging guardrails
        options.DefaultPageSize = Configuration.ReadFirst(
            config,
            defaultValue: options.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);
        options.MaxPageSize = Configuration.ReadFirst(
            config,
            defaultValue: options.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);
        // Governance
        var ddlStr = Configuration.ReadFirst(
            config,
            defaultValue: options.DdlPolicy.ToString(),
            Infrastructure.Constants.Configuration.Keys.DdlPolicy,
            Infrastructure.Constants.Configuration.Keys.AltDdlPolicy);
        if (!string.IsNullOrWhiteSpace(ddlStr))
        {
            if (Enum.TryParse<SchemaDdlPolicy>(ddlStr, ignoreCase: true, out var ddl)) options.DdlPolicy = ddl;
        }
        var smStr = Configuration.ReadFirst(
            config,
            defaultValue: options.SchemaMatching.ToString(),
            Infrastructure.Constants.Configuration.Keys.SchemaMatchingMode,
            Infrastructure.Constants.Configuration.Keys.AltSchemaMatchingMode);
        if (!string.IsNullOrWhiteSpace(smStr))
        {
            if (Enum.TryParse<SchemaMatchingMode>(smStr, ignoreCase: true, out var sm)) options.SchemaMatching = sm;
        }
        // Magic flag for production overrides
        options.AllowProductionDdl = Configuration.Read(
            config,
            Constants.Configuration.Koan.AllowMagicInProduction,
            options.AllowProductionDdl);
    }
}