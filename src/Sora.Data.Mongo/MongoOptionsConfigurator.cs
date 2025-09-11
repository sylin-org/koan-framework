using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using Sora.Core;

namespace Sora.Data.Mongo;

/// <summary>
/// Auto-registration for Mongo adapter and health contributor during Sora initialization.
/// </summary>
// legacy initializer removed in favor of standardized auto-registrar

internal sealed class MongoOptionsConfigurator(IConfiguration config, ILogger<MongoOptionsConfigurator> logger) : IConfigureOptions<MongoOptions>
{
    public void Configure(MongoOptions options)
    {
        logger.LogInformation("MongoDB Auto-Configuration Started");
        logger.LogInformation("Environment: {Environment}, InContainer: {InContainer}, IsProduction: {IsProduction}", 
            SoraEnv.EnvironmentName, SoraEnv.InContainer, SoraEnv.IsProduction);
        logger.LogInformation("Initial options - ConnectionString: '{ConnectionString}', Database: '{Database}'", 
            options.ConnectionString, options.Database);
        
        // Phase 1: Handle explicit configuration (highest priority)
        var explicitConnectionString = Configuration.ReadFirst(
            config,
            defaultValue: string.Empty,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsMongo,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);
            
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            logger.LogInformation("Using explicit connection string from configuration: '{ConnectionString}'", explicitConnectionString);
            options.ConnectionString = explicitConnectionString;
        }
        else if (string.Equals(options.ConnectionString.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Auto-detection mode activated - resolving MongoDB connection...");
            options.ConnectionString = ResolveAutoConnection(logger);
        }
        else if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            logger.LogInformation("No connection string provided - falling back to auto-detection");
            options.ConnectionString = ResolveAutoConnection(logger);
        }
        else
        {
            logger.LogInformation("Using pre-configured connection string: '{ConnectionString}'", options.ConnectionString);
        }
        // Configure other options
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

        // Final connection string normalization and logging
        options.ConnectionString = NormalizeConnectionString(options.ConnectionString);
        logger.LogInformation("Final MongoDB Configuration");
        logger.LogInformation("Connection: {ConnectionString}", options.ConnectionString);
        logger.LogInformation("Database: {Database}", options.Database);
        logger.LogInformation("MongoDB Auto-Configuration Complete");
    }

    private string ResolveAutoConnection(ILogger logger)
    {
        // Check if auto-detection is explicitly disabled first
        if (IsAutoDetectionDisabled())
        {
            logger.LogInformation("Auto-detection disabled via configuration - using localhost");
            return MongoConstants.DefaultLocalUri;
        }

        // Phase 1: Environment variable list (highest priority for auto-detection)
        var envConnection = TryEnvironmentVariableList(logger);
        if (!string.IsNullOrWhiteSpace(envConnection)) 
        {
            return envConnection;
        }

        // Phase 2: ConnectionStrings:Default fallback
        var defaultConnection = TryDefaultConnectionString(logger);
        if (!string.IsNullOrWhiteSpace(defaultConnection)) 
        {
            return defaultConnection;
        }

        // Phase 3: Smart environment-based auto-detection
        return ResolveByEnvironment(logger);
    }

    private bool IsAutoDetectionDisabled()
    {
        return Configuration.Read(config, "Sora:Data:Mongo:DisableAutoDetection", false)
               || Configuration.Read(config, "SORA_DATA_MONGO_DISABLE_AUTO_DETECTION", false);
    }

    private string? TryEnvironmentVariableList(ILogger logger)
    {
        try
        {
            var list = Environment.GetEnvironmentVariable(MongoConstants.EnvList);
            if (string.IsNullOrWhiteSpace(list))
            {
                logger.LogInformation("Environment variable {EnvList} not set", MongoConstants.EnvList);
                return null;
            }

            logger.LogInformation("Testing MongoDB URLs from {EnvList}: {List}", MongoConstants.EnvList, list);
            foreach (var part in list.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = part.Trim();
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                
                var normalized = NormalizeConnectionString(candidate);
                logger.LogInformation("  Testing: {ConnectionString}", normalized);
                
                if (TryMongoPing(normalized, TimeSpan.FromMilliseconds(500)))
                {
                    logger.LogInformation("  SUCCESS: {ConnectionString} is reachable", normalized);
                    return normalized;
                }
                logger.LogInformation("  Failed: {ConnectionString} not reachable", normalized);
            }
            logger.LogInformation("No URLs from environment variable were reachable");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error processing {EnvList}: {Message}", MongoConstants.EnvList, ex.Message);
        }
        return null;
    }

    private string? TryDefaultConnectionString(ILogger logger)
    {
        var cs = Configuration.Read(config, Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault, null);
        if (!string.IsNullOrWhiteSpace(cs))
        {
            logger.LogInformation("Found ConnectionStrings:Default = '{ConnectionString}'", cs);
            return cs;
        }
        logger.LogInformation("No ConnectionStrings:Default found");
        return null;
    }

    private string ResolveByEnvironment(ILogger logger)
    {
        var isProd = SoraEnv.IsProduction;
        var inContainer = SoraEnv.InContainer;

        logger.LogInformation("Environment-based resolution: Production={Production}, Container={Container}", isProd, inContainer);
        
        if (isProd)
        {
            logger.LogInformation("Production environment: using secure default {DefaultUri}", MongoConstants.DefaultLocalUri);
            return MongoConstants.DefaultLocalUri;
        }

        // Development environment: try smart detection with connectivity testing
        logger.LogInformation("Development environment: testing connectivity...");
        
        var candidates = new[]
        {
            (MongoConstants.DefaultComposeUri, "container/compose hostname"),
            (MongoConstants.DefaultLocalUri, "localhost")
        };

        foreach (var (uri, description) in candidates)
        {
            logger.LogInformation("  Testing {Description}: {Uri}", description, uri);
            if (TryMongoPing(uri, TimeSpan.FromMilliseconds(500)))
            {
                logger.LogInformation("  SUCCESS: {Description} is reachable", description);
                return uri;
            }
            logger.LogInformation("  Failed: {Description} not reachable", description);
        }

        // Nothing reachable - choose intelligent fallback
        var fallback = inContainer ? MongoConstants.DefaultComposeUri : MongoConstants.DefaultLocalUri;
        var reason = inContainer ? "container environment detected" : "bare metal environment detected";
        logger.LogInformation("No MongoDB reachable - intelligent fallback: {Fallback} ({Reason})", fallback, reason);
        return fallback;
    }

    private static string NormalizeConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString)) return connectionString;
        
        var trimmed = connectionString.Trim();
        if (trimmed.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }
        
        return "mongodb://" + trimmed;
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