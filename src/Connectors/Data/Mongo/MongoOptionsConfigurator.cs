using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Mongo.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Mongo;

internal sealed class MongoOptionsConfigurator(IConfiguration configuration) : IConfigureOptions<MongoOptions>
{
    public void Configure(MongoOptions options)
    {
        var owner = configuration["Koan:Data:Sources:Default:Adapter"];
        var generic = string.IsNullOrWhiteSpace(owner) || MongoAdapterFactory.HandlesProvider(owner);
        var connection = generic
            ? First(
                "Koan:Data:Sources:Default:ConnectionString",
                Constants.Configuration.DefaultSourceConnectionString,
                Constants.Configuration.ConnectionString,
                Constants.Configuration.StandardConnectionString)
            : First(
                Constants.Configuration.DefaultSourceConnectionString,
                Constants.Configuration.ConnectionString,
                Constants.Configuration.StandardConnectionString);
        var database = generic
            ? First(
                "Koan:Data:Sources:Default:Database",
                Constants.Configuration.DefaultSourceDatabase,
                Constants.Configuration.Database)
            : First(Constants.Configuration.DefaultSourceDatabase, Constants.Configuration.Database);
        if (!string.IsNullOrWhiteSpace(database)) options.Database = database.Trim();

        var requested = connection ?? options.ConnectionString;
        options.ConnectionString = string.IsNullOrWhiteSpace(requested)
            ? Constants.Configuration.Auto
            : requested.Trim();

        if (Enum.TryParse<StorageNamingStyle>(First(
                "Koan:Data:Sources:Default:mongo:NamingStyle",
                Constants.Configuration.Section + ":NamingStyle"), true, out var naming))
            options.NamingStyle = naming;
        options.Separator = First(
            "Koan:Data:Sources:Default:mongo:Separator",
            Constants.Configuration.Section + ":Separator") ?? options.Separator;
    }

    private string? First(params string[] keys)
    {
        foreach (var key in keys)
            if (configuration[key] is { } value && !string.IsNullOrWhiteSpace(value)) return value;
        return null;
    }

}
