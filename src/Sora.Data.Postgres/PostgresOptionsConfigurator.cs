using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Core.Infrastructure;

namespace Sora.Data.Postgres;

internal sealed class PostgresOptionsConfigurator(IConfiguration config) : IConfigureOptions<PostgresOptions>
{
    public void Configure(PostgresOptions options)
    {
        options.ConnectionString = Configuration.ReadFirst(
            config,
            defaultValue: options.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsPostgres,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);

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

        var ddlStr = Configuration.ReadFirst(config, options.DdlPolicy.ToString(),
            Infrastructure.Constants.Configuration.Keys.DdlPolicy,
            Infrastructure.Constants.Configuration.Keys.AltDdlPolicy);
        if (!string.IsNullOrWhiteSpace(ddlStr) && Enum.TryParse<SchemaDdlPolicy>(ddlStr, true, out var ddl)) options.DdlPolicy = ddl;

        var smStr = Configuration.ReadFirst(config, options.SchemaMatching.ToString(),
            Infrastructure.Constants.Configuration.Keys.SchemaMatchingMode,
            Infrastructure.Constants.Configuration.Keys.AltSchemaMatchingMode);
        if (!string.IsNullOrWhiteSpace(smStr) && Enum.TryParse<SchemaMatchingMode>(smStr, true, out var sm)) options.SchemaMatching = sm;

        options.AllowProductionDdl = Configuration.Read(
            config,
            Constants.Configuration.Sora.AllowMagicInProduction,
            options.AllowProductionDdl);

        var sp = Configuration.ReadFirst(config, options.SearchPath ?? "public",
            Infrastructure.Constants.Configuration.Keys.SearchPath);
        options.SearchPath = sp;
    }
}