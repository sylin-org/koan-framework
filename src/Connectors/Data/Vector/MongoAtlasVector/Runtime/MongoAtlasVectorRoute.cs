using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Sources;
using Koan.Data.Core;
using Microsoft.Extensions.Configuration;

namespace Koan.Data.Vector.Connector.MongoAtlasVector;

internal sealed record MongoAtlasVectorRoute(
    string Source,
    string ConnectionString,
    string Database,
    string Origin,
    DataSourcePlan Policy)
{
    internal static MongoAtlasVectorRoute Resolve(
        IConfiguration configuration,
        DataSourceRegistry sources,
        MongoAtlasVectorOptions options,
        IAdapterFactory owner,
        string source)
    {
        var name = string.IsNullOrWhiteSpace(source) ? "Default" : source;
        var definition = sources.GetSource(name);
        var connection = ProviderScoped(configuration, owner, name, "ConnectionString");
        var origin = "MongoAtlasVector source";

        if (connection is null && OwnsVectorSource(owner, definition?.Adapter))
        {
            connection = Concrete(definition?.ConnectionString)
                ?? Concrete(configuration[$"Koan:Data:Sources:{name}:ConnectionString"])
                ?? Concrete(configuration.GetConnectionString(name));
        }

        if (connection is null && BelongsToMongo(definition?.Adapter))
        {
            connection = Concrete(definition?.ConnectionString)
                ?? Concrete(configuration.GetConnectionString(name))
                ?? Concrete(configuration[$"Koan:Data:Sources:{name}:mongo:ConnectionString"]);
            origin = "paired Mongo source";
        }

        connection ??= Concrete(options.ConnectionString)
            ?? Concrete(configuration.GetConnectionString("MongoAtlasVector"));
        if (connection is not null) origin = origin == "paired Mongo source" ? origin : "MongoAtlasVector";
        connection ??= Concrete(configuration[Infrastructure.Constants.Configuration.PairedConnectionString])
            ?? Concrete(configuration.GetConnectionString("Mongo"));
        if (connection is not null && origin == "MongoAtlasVector source") origin = "paired Mongo";
        connection ??= Infrastructure.Constants.Configuration.Automatic;

        var database = ResolveDatabase(configuration, definition, owner, name, options.Database);
        var identity = connection + "|database=" + database;
        return new MongoAtlasVectorRoute(
            name,
            connection,
            database,
            origin,
            sources.GetPlan(name, Infrastructure.Constants.Provider.Name, identity));
    }

    internal static (string ConnectionString, string Database, string Origin) ResolveDefault(
        IConfiguration configuration)
    {
        var own = Concrete(configuration[Infrastructure.Constants.Configuration.Keys.ConnectionString])
            ?? Concrete(configuration.GetConnectionString("MongoAtlasVector"));
        if (own is not null)
            return (own,
                Concrete(configuration[Infrastructure.Constants.Configuration.Keys.Database]) ?? Infrastructure.Constants.Defaults.Database,
                "MongoAtlasVector");
        var paired = Concrete(configuration[Infrastructure.Constants.Configuration.PairedConnectionString])
            ?? Concrete(configuration.GetConnectionString("Mongo"));
        return (paired ?? Infrastructure.Constants.Configuration.Automatic,
            Concrete(configuration[Infrastructure.Constants.Configuration.Keys.Database]) ?? Infrastructure.Constants.Defaults.Database,
            paired is null ? "automatic Mongo discovery" : "paired Mongo");
    }

    private static string ResolveDatabase(
        IConfiguration configuration,
        DataSourceRegistry.SourceDefinition? definition,
        IAdapterFactory owner,
        string source,
        string configuredDefault)
    {
        var scoped = ProviderScoped(configuration, owner, source, "Database");
        if (scoped is not null) return scoped;
        if (definition is not null && OwnsVectorSource(owner, definition.Adapter) &&
            definition.Settings.TryGetValue("Database", out var direct) && Concrete(direct) is { } database)
            return database;
        return Concrete(configuration[Infrastructure.Constants.Configuration.Keys.Database])
            ?? Concrete(configuredDefault)
            ?? Infrastructure.Constants.Defaults.Database;
    }

    private static bool BelongsToMongo(string? provider) => provider is not null &&
        (provider.Equals(Infrastructure.Constants.Provider.PairedMongo, StringComparison.OrdinalIgnoreCase) ||
         provider.Equals("mongodb", StringComparison.OrdinalIgnoreCase));

    private static bool OwnsVectorSource(IAdapterFactory owner, string? provider) =>
        provider is not null && !BelongsToMongo(provider) &&
        (provider.Equals(owner.Provider, StringComparison.OrdinalIgnoreCase) ||
         owner.Aliases.Contains(provider, StringComparer.OrdinalIgnoreCase));

    private static string? ProviderScoped(
        IConfiguration configuration,
        IAdapterFactory owner,
        string source,
        string setting)
    {
        foreach (var provider in new[] { owner.Provider }.Concat(owner.Aliases)
                     .Where(static provider => !provider.Equals("mongo", StringComparison.OrdinalIgnoreCase) &&
                                               !provider.Equals("mongodb", StringComparison.OrdinalIgnoreCase))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var value = Concrete(configuration[$"Koan:Data:Sources:{source}:{provider}:{setting}"]);
            if (value is not null) return value;
        }
        return null;
    }

    private static string? Concrete(string? value) =>
        string.IsNullOrWhiteSpace(value) ||
        value.Trim().Equals(Infrastructure.Constants.Configuration.Automatic, StringComparison.OrdinalIgnoreCase)
            ? null
            : value.Trim();
}
