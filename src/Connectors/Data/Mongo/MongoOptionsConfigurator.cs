using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Koan.Data.Abstractions.Naming;
using Koan.Data.Connector.Mongo.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Koan.Data.Connector.Mongo;

internal sealed class MongoOptionsConfigurator(
    IConfiguration configuration,
    IServiceDiscoveryCoordinator? discovery = null) : IConfigureOptions<MongoOptions>
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
        options.ConnectionString = IsAuto(requested)
            ? Discover(options.Database)
            : IsZenGarden(requested)
                ? ResolveRequired(requested!.Trim(), options.Database)
                : requested!.Trim();

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

    private string Discover(string database)
    {
        var fallback = $"mongodb://localhost:{Constants.Discovery.DefaultPort}";
        if (configuration.GetValue(Constants.Configuration.DisableAutoDetection, false) || discovery is null)
            return fallback;
        var result = discovery.DiscoverService(
                Constants.Discovery.ServiceName,
                Context(database))
            .GetAwaiter().GetResult();
        return result.IsSuccessful && !string.IsNullOrWhiteSpace(result.ServiceUrl)
            ? result.ServiceUrl
            : fallback;
    }

    private string ResolveRequired(string intent, string database)
    {
        if (discovery is null)
            throw ExplicitIntentFailure("Koan's service-discovery coordinator is unavailable.");
        var result = discovery.ResolveServiceIntent(
                Constants.Discovery.ServiceName,
                intent,
                Context(database))
            .GetAwaiter().GetResult();
        if (!result.IsSuccessful || string.IsNullOrWhiteSpace(result.ServiceUrl))
            throw ExplicitIntentFailure(result.ErrorMessage);
        return result.ServiceUrl;
    }

    private DiscoveryContext Context(string database) => new()
    {
        Configuration = configuration,
        RequireHealthValidation = true,
        Parameters = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["database"] = database
        }
    };

    private static InvalidOperationException ExplicitIntentFailure(string? reason) => new(
        "Mongo explicit Zen Garden intent for 'mongodb' could not be satisfied. " +
        $"{reason ?? "No ready MongoDB offering was found."} " +
        "Reference and enable Koan.ZenGarden with a ready 'mongodb' offering, choose 'auto', " +
        "or provide a native MongoDB connection string.");

    private static bool IsAuto(string? value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value.Trim(), "auto", StringComparison.OrdinalIgnoreCase);

    private static bool IsZenGarden(string? value) =>
        value?.Trim().StartsWith("zen-garden://", StringComparison.OrdinalIgnoreCase) == true;
}
