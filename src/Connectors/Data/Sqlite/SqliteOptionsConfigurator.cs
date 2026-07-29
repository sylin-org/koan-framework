using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Sqlite.Infrastructure;
using Koan.Data.Relational.Orchestration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Sqlite;

internal sealed class SqliteOptionsConfigurator(
    IConfiguration configuration,
    IServiceDiscoveryCoordinator? discovery = null) : IConfigureOptions<SqliteOptions>
{
    public void Configure(SqliteOptions options)
    {
        var owner = configuration["Koan:Data:Sources:Default:Adapter"];
        var genericBelongs = string.IsNullOrWhiteSpace(owner) || SqliteAdapterFactory.HandlesProvider(owner);
        var requested = genericBelongs
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

        requested = string.IsNullOrWhiteSpace(requested) ? options.ConnectionString : requested;
        options.ConnectionString = IsAuto(requested) ? Discover() : requested!;

        options.NamingStyle = EnumValue(
            options.NamingStyle,
            Constants.Configuration.Keys.ProviderNamingStyle,
            Constants.Configuration.Keys.NamingStyle);
        options.Separator = First(
            Constants.Configuration.Keys.ProviderSeparator,
            Constants.Configuration.Keys.Separator) ?? options.Separator;
        options.DdlPolicy = EnumValue(
            options.DdlPolicy,
            Constants.Configuration.Keys.ProviderDdlPolicy,
            Constants.Configuration.Keys.DdlPolicy);
        options.SchemaMatching = EnumValue(
            options.SchemaMatching,
            Constants.Configuration.Keys.ProviderSchemaMatching,
            Constants.Configuration.Keys.SchemaMatching);
        options.AllowProductionDdl = options.DdlPolicy == RelationalDdlPolicy.AutoCreate;
    }

    private string Discover()
    {
        if (configuration.GetValue(Constants.Configuration.Keys.DisableAutoDetection, false) || discovery is null)
            return Constants.DefaultConnection;

        var result = discovery.DiscoverService(
                Constants.Provider,
                new DiscoveryContext { Configuration = configuration, RequireHealthValidation = false })
            .GetAwaiter().GetResult();
        return result.IsSuccessful && !string.IsNullOrWhiteSpace(result.ServiceUrl)
            ? result.ServiceUrl
            : Constants.DefaultConnection;
    }

    private T EnumValue<T>(T fallback, params string[] keys) where T : struct, Enum
    {
        var value = First(keys);
        return Enum.TryParse<T>(value, ignoreCase: true, out var parsed) ? parsed : fallback;
    }

    private string? First(params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = configuration[key];
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return null;
    }

    private static bool IsAuto(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "auto", StringComparison.OrdinalIgnoreCase);
}
