using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;

namespace Koan.Data.Vector.Connector.SqliteVec;

internal sealed record SqliteVecRoute(
    string Source,
    string ConnectionString,
    string Origin,
    DataSourcePlan Policy)
{
    internal static SqliteVecRoute Resolve(
        IConfiguration configuration,
        DataSourceRegistry sources,
        SqliteVecOptions options,
        IAdapterFactory owner,
        string source)
    {
        var name = string.IsNullOrWhiteSpace(source) ? "Default" : source;
        var own = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration,
            sources,
            Infrastructure.Constants.Provider.Name,
            name,
            options.ConnectionString,
            owner);
        var connection = own;
        var origin = "SqliteVec";
        if (IsAutomatic(connection))
        {
            var pairedDefault = FirstConcrete(
                configuration[Infrastructure.Constants.Configuration.PairedConnectionString],
                configuration.GetConnectionString("Sqlite"),
                Infrastructure.Constants.Configuration.LocalFallback);
            connection = AdapterConnectionResolver.ResolveRoutedConnection(
                configuration,
                sources,
                Infrastructure.Constants.Provider.PairedDataProvider,
                name,
                pairedDefault,
                owner);
            origin = "paired SQLite";
        }

        if (IsAutomatic(connection))
            throw new InvalidOperationException(
                $"SqliteVec source '{name}' has no concrete placement. Configure its ConnectionString or a paired SQLite source.");
        return new SqliteVecRoute(
            name,
            connection,
            origin,
            sources.GetPlan(name, Infrastructure.Constants.Provider.Name));
    }

    internal static (string ConnectionString, string Origin) ResolveDefault(IConfiguration configuration)
    {
        var own = FirstConcrete(
            configuration[Infrastructure.Constants.Configuration.Section + ":ConnectionString"],
            configuration.GetConnectionString("SqliteVec"));
        if (!IsAutomatic(own)) return (own!, "SqliteVec");
        return (FirstConcrete(
            configuration[Infrastructure.Constants.Configuration.PairedConnectionString],
            configuration.GetConnectionString("Sqlite"),
            Infrastructure.Constants.Configuration.LocalFallback)!, "paired SQLite");
    }

    private static string? FirstConcrete(params string?[] values) =>
        values.FirstOrDefault(static value => !IsAutomatic(value));

    private static bool IsAutomatic(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        string.Equals(value.Trim(), Infrastructure.Constants.Configuration.Automatic, StringComparison.OrdinalIgnoreCase);
}
