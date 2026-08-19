using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Koan.Redis;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Koan.Data.Vector.Connector.RedisVector;

internal sealed record RedisVectorRoute(
    string Source,
    string ConnectionString,
    IConnectionMultiplexer Connection,
    DataSourcePlan Policy)
{
    internal IDatabase Data => Connection.GetDatabase(Infrastructure.Constants.Defaults.Database);

    internal static RedisVectorRoute Resolve(
        IConfiguration configuration,
        DataSourceRegistry sources,
        IRedisConnectionProvider connections,
        IAdapterFactory owner,
        string source)
    {
        var name = string.IsNullOrWhiteSpace(source) ? "Default" : source;
        var definition = sources.GetSource(name);
        var routeProvider = string.Equals(
            definition?.Adapter,
            Infrastructure.Constants.Provider.PairedRedis,
            StringComparison.OrdinalIgnoreCase)
                ? Infrastructure.Constants.Provider.PairedRedis
                : Infrastructure.Constants.Provider.Name;
        var connection = AdapterConnectionResolver.ResolveRoutedConnection(
            configuration,
            sources,
            routeProvider,
            name,
            connections.DefaultConnectionString,
            owner);
        var database = ResolveDatabase(configuration, sources, name);
        if (database != Infrastructure.Constants.Defaults.Database)
            throw new InvalidOperationException(
                $"RedisVector source '{name}' requests Redis database {database}, but Redis Search vector indexes are supported only in database 0. " +
                "Keep the same Redis endpoint and configure this vector source with Database=0.");
        return new RedisVectorRoute(
            name,
            connection,
            connections.GetConnection(connection),
            sources.GetPlan(name, Infrastructure.Constants.Provider.Name, connection));
    }

    private static int ResolveDatabase(
        IConfiguration configuration,
        DataSourceRegistry sources,
        string source)
    {
        var scoped = configuration[Infrastructure.Constants.Configuration.SourceDatabase(source)];
        if (!string.IsNullOrWhiteSpace(scoped)) return ParseDatabase(scoped, source);

        var definition = sources.GetSource(source);
        if (definition is not null && string.Equals(
                definition.Adapter,
                Infrastructure.Constants.Provider.Name,
                StringComparison.OrdinalIgnoreCase) &&
            definition.Settings.TryGetValue("Database", out var direct))
            return ParseDatabase(direct, source);

        // A paired `redis` source owns its logical database for record/cache work. RedisVector shares
        // the endpoint and multiplexer but intentionally addresses Search's supported database 0.
        return Infrastructure.Constants.Defaults.Database;
    }

    private static int ParseDatabase(string value, string source)
    {
        if (int.TryParse(value, out var database)) return database;
        throw new InvalidOperationException(
            $"RedisVector source '{source}' has invalid Database value '{value}'. Use Database=0.");
    }
}
