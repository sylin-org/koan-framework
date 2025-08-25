using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Sora.Core;

namespace Sora.Data.Mongo;

/// <summary>
/// Auto-registration for Mongo adapter and health contributor during Sora initialization.
/// </summary>
// legacy initializer removed in favor of standardized auto-registrar

internal sealed class MongoOptionsConfigurator(IConfiguration config) : IConfigureOptions<MongoOptions>
{
    public void Configure(MongoOptions options)
    {
        // Bind provider-specific options using Configuration helper (ADR-0040)
        options.ConnectionString = Configuration.ReadFirst(
            config,
            defaultValue: options.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsMongo,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);
        options.Database = Configuration.ReadFirst(
            config,
            defaultValue: options.Database,
            Infrastructure.Constants.Configuration.Keys.Database,
            Infrastructure.Constants.Configuration.Keys.AltDatabase);

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

        // If an env list is provided, use the first reachable entry
        try
        {
            var list = Environment.GetEnvironmentVariable(MongoConstants.EnvList);
            if (!string.IsNullOrWhiteSpace(list))
            {
                foreach (var part in list.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = part.Trim();
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    // Normalize scheme if missing
                    var normalized = candidate.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) || candidate.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase)
                        ? candidate
                        : ("mongodb://" + candidate);
                    if (TryMongoPing(normalized, TimeSpan.FromMilliseconds(250))) { options.ConnectionString = normalized; break; }
                }
            }
        }
        catch { /* best-effort */ }

        // Resolve from ConnectionStrings:Default when present. Override placeholder/empty.
        var cs = Configuration.Read(config, Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault, null);
        if (!string.IsNullOrWhiteSpace(cs))
        {
            if (string.IsNullOrWhiteSpace(options.ConnectionString) || string.Equals(options.ConnectionString.Trim(), MongoConstants.DefaultLocalUri, StringComparison.OrdinalIgnoreCase))
            {
                options.ConnectionString = cs!;
            }
        }
        // Final safety default if still unset or sentinel 'auto': prefer docker compose host when containerized
        if (string.IsNullOrWhiteSpace(options.ConnectionString) || string.Equals(options.ConnectionString.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            var inContainer = SoraEnv.InContainer;
            options.ConnectionString = inContainer ? MongoConstants.DefaultComposeUri : MongoConstants.DefaultLocalUri;
        }

        // Normalize: ensure mongodb scheme is present to avoid driver showing "Unspecified/host:port"
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            var v = options.ConnectionString.Trim();
            if (!v.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) &&
                !v.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
            {
                options.ConnectionString = "mongodb://" + v;
            }
        }
    }

    private static bool TryMongoPing(string connectionString, TimeSpan timeout)
    {
        try
        {
            var settings = MongoClientSettings.FromConnectionString(connectionString);
            settings.ServerSelectionTimeout = timeout;
            var client = new MongoClient(settings);
            // ping admin
            client.GetDatabase("admin").RunCommand<BsonDocument>(new BsonDocument("ping", 1));
            return true;
        }
        catch { return false; }
    }

    // Container detection uses SoraEnv static runtime snapshot per ADR-0039
}