using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Koan.Data.Relational.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Sqlite;

internal sealed class SqliteOptionsSetup(IConfiguration configuration) : IConfigureOptions<SqliteOptions>
{
    public void Configure(SqliteOptions options)
    {
        var owner = configuration["Koan:Data:Sources:Default:Adapter"];
        var ownsDefault = string.IsNullOrWhiteSpace(owner) || SqliteAdapterFactory.HandlesProvider(owner);
        var requested = ownsDefault
            ? First(
                Constants.Configuration.Keys.DefaultSourceConnectionString,
                Constants.Configuration.Keys.ProviderSourceConnectionString,
                Constants.Configuration.Keys.ConnectionString,
                Constants.Configuration.Keys.ConnectionStringsSqlite,
                Constants.Configuration.Keys.ConnectionStringsDefault)
            : First(
                Constants.Configuration.Keys.ProviderSourceConnectionString,
                Constants.Configuration.Keys.ConnectionString,
                Constants.Configuration.Keys.ConnectionStringsSqlite);

        var candidate = requested ?? options.ConnectionString;
        options.ConnectionString = IsAuto(candidate)
            ? Constants.DefaultConnection
            : candidate.Trim();
        options.NamingStyle = ReadEnum(options.NamingStyle,
            Constants.Configuration.Keys.ProviderNamingStyle,
            Constants.Configuration.Keys.NamingStyle);
        options.Separator = First(
            Constants.Configuration.Keys.ProviderSeparator,
            Constants.Configuration.Keys.Separator) ?? options.Separator;
        options.DdlPolicy = ReadEnum(options.DdlPolicy,
            Constants.Configuration.Keys.ProviderDdlPolicy,
            Constants.Configuration.Keys.DdlPolicy);
        options.SchemaMatching = ReadEnum(options.SchemaMatching,
            Constants.Configuration.Keys.ProviderSchemaMatching,
            Constants.Configuration.Keys.SchemaMatching);
        options.AllowProductionDdl = options.DdlPolicy == RelationalDdlPolicy.AutoCreate;
    }

    private T ReadEnum<T>(T fallback, params string[] keys) where T : struct, Enum =>
        Enum.TryParse<T>(First(keys), true, out var value) ? value : fallback;

    private string? First(params string[] keys)
    {
        foreach (var key in keys)
            if (configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)) return value.Trim();
        return null;
    }

    private static bool IsAuto(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
}
