using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Core.Infrastructure;

namespace Sora.Data.SqlServer;

internal sealed class SqlServerOptionsConfigurator(IConfiguration config) : IConfigureOptions<SqlServerOptions>
{
    public void Configure(SqlServerOptions options)
    {
        options.ConnectionString = Configuration.ReadFirst(
            config,
            defaultValue: options.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsSqlServer,
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

        // Serialization/materialization options
        options.JsonCaseInsensitive = Configuration.Read(
            config,
            Infrastructure.Constants.Configuration.Keys.JsonCaseInsensitive,
            options.JsonCaseInsensitive);
        options.JsonWriteIndented = Configuration.Read(
            config,
            Infrastructure.Constants.Configuration.Keys.JsonWriteIndented,
            options.JsonWriteIndented);
        options.JsonIgnoreNullValues = Configuration.Read(
            config,
            Infrastructure.Constants.Configuration.Keys.JsonIgnoreNullValues,
            options.JsonIgnoreNullValues);

        options.AllowProductionDdl = Configuration.Read(
            config,
            Constants.Configuration.Sora.AllowMagicInProduction,
            options.AllowProductionDdl);
    }
}