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
        void DebugLog(string msg)
        {
            try { Console.WriteLine($"[MongoDB][AUTO-DETECT] {msg}"); } catch { }
        }
        
        DebugLog($"=== MongoDB Auto-Configuration Started ===");
        DebugLog($"Environment: {SoraEnv.EnvironmentName}, InContainer: {SoraEnv.InContainer}, IsProduction: {SoraEnv.IsProduction}");
        DebugLog($"Initial options - ConnectionString: '{options.ConnectionString}', Database: '{options.Database}'");
        
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
            DebugLog($"✓ Using explicit connection string from configuration: '{explicitConnectionString}'");
            options.ConnectionString = explicitConnectionString;
        }
        else if (string.Equals(options.ConnectionString.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            DebugLog("🔍 Auto-detection mode activated - resolving MongoDB connection...");
            options.ConnectionString = ResolveAutoConnection(DebugLog);
        }
        else if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            DebugLog("⚠️  No connection string provided - falling back to auto-detection");
            options.ConnectionString = ResolveAutoConnection(DebugLog);
        }
        else
        {
            DebugLog($"✓ Using pre-configured connection string: '{options.ConnectionString}'");
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
        DebugLog($"=== Final MongoDB Configuration ===");
        DebugLog($"✓ Connection: {options.ConnectionString}");
        DebugLog($"✓ Database: {options.Database}");
        DebugLog($"=== MongoDB Auto-Configuration Complete ===");
    }

    private string ResolveAutoConnection(Action<string> debugLog)
    {
        // Check if auto-detection is explicitly disabled first
        if (IsAutoDetectionDisabled())
        {
            debugLog("❌ Auto-detection disabled via configuration - using localhost");
            return MongoConstants.DefaultLocalUri;
        }

        // Phase 1: Environment variable list (highest priority for auto-detection)
        var envConnection = TryEnvironmentVariableList(debugLog);
        if (!string.IsNullOrWhiteSpace(envConnection)) 
        {
            return envConnection;
        }

        // Phase 2: ConnectionStrings:Default fallback
        var defaultConnection = TryDefaultConnectionString(debugLog);
        if (!string.IsNullOrWhiteSpace(defaultConnection)) 
        {
            return defaultConnection;
        }

        // Phase 3: Smart environment-based auto-detection
        return ResolveByEnvironment(debugLog);
    }

    private bool IsAutoDetectionDisabled()
    {
        return Configuration.Read(config, "Sora:Data:Mongo:DisableAutoDetection", false)
               || Configuration.Read(config, "SORA_DATA_MONGO_DISABLE_AUTO_DETECTION", false);
    }

    private string? TryEnvironmentVariableList(Action<string> debugLog)
    {
        try
        {
            var list = Environment.GetEnvironmentVariable(MongoConstants.EnvList);
            if (string.IsNullOrWhiteSpace(list))
            {
                debugLog($"📝 Environment variable {MongoConstants.EnvList} not set");
                return null;
            }

            debugLog($"🔍 Testing MongoDB URLs from {MongoConstants.EnvList}: {list}");
            foreach (var part in list.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = part.Trim();
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                
                var normalized = NormalizeConnectionString(candidate);
                debugLog($"  Testing: {normalized}");
                
                if (TryMongoPing(normalized, TimeSpan.FromMilliseconds(500)))
                {
                    debugLog($"  ✅ SUCCESS: {normalized} is reachable");
                    return normalized;
                }
                debugLog($"  ❌ Failed: {normalized} not reachable");
            }
            debugLog("❌ No URLs from environment variable were reachable");
        }
        catch (Exception ex)
        {
            debugLog($"⚠️  Error processing {MongoConstants.EnvList}: {ex.Message}");
        }
        return null;
    }

    private string? TryDefaultConnectionString(Action<string> debugLog)
    {
        var cs = Configuration.Read(config, Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault, null);
        if (!string.IsNullOrWhiteSpace(cs))
        {
            debugLog($"🔍 Found ConnectionStrings:Default = '{cs}'");
            return cs;
        }
        debugLog("📝 No ConnectionStrings:Default found");
        return null;
    }

    private string ResolveByEnvironment(Action<string> debugLog)
    {
        var isProd = SoraEnv.IsProduction;
        var inContainer = SoraEnv.InContainer;

        debugLog($"🌍 Environment-based resolution: Production={isProd}, Container={inContainer}");
        
        if (isProd)
        {
            debugLog($"🔒 Production environment: using secure default {MongoConstants.DefaultLocalUri}");
            return MongoConstants.DefaultLocalUri;
        }

        // Development environment: try smart detection with connectivity testing
        debugLog("🧪 Development environment: testing connectivity...");
        
        var candidates = new[]
        {
            (MongoConstants.DefaultComposeUri, "container/compose hostname"),
            (MongoConstants.DefaultLocalUri, "localhost")
        };

        foreach (var (uri, description) in candidates)
        {
            debugLog($"  Testing {description}: {uri}");
            if (TryMongoPing(uri, TimeSpan.FromMilliseconds(500)))
            {
                debugLog($"  ✅ SUCCESS: {description} is reachable");
                return uri;
            }
            debugLog($"  ❌ Failed: {description} not reachable");
        }

        // Nothing reachable - choose intelligent fallback
        var fallback = inContainer ? MongoConstants.DefaultComposeUri : MongoConstants.DefaultLocalUri;
        var reason = inContainer ? "container environment detected" : "bare metal environment detected";
        debugLog($"⚠️  No MongoDB reachable - intelligent fallback: {fallback} ({reason})");
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