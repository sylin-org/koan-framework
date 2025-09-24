using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Koan.Core.Orchestration;

namespace Koan.Data.Redis;

internal sealed class RedisOptionsConfigurator : IConfigureOptions<RedisOptions>
{
    private readonly IConfiguration? _cfg;
    public RedisOptionsConfigurator() { }
    public RedisOptionsConfigurator(IConfiguration cfg) { _cfg = cfg; }
    public void Configure(RedisOptions o)
    {
        // Use orchestration-aware connection resolution
        var resolver = new OrchestrationAwareConnectionResolver(_cfg);
        var hints = new OrchestrationConnectionHints
        {
            SelfOrchestrated = Infrastructure.Constants.Discovery.DefaultLocal,     // localhost:6379
            DockerCompose = Infrastructure.Constants.Discovery.DefaultCompose,      // redis:6379
            Kubernetes = "redis.default.svc.cluster.local:6379",                  // K8s service DNS
            AspireManaged = null,                                                  // Aspire will provide via service discovery
            External = null,                                                       // Must be explicitly configured
            DefaultPort = 6379,
            ServiceName = "redis"
        };

        // Check for explicit connection string first (environment variables, multi-endpoint lists)
        var explicitConnectionString = CheckExplicitConnectionStrings();
        if (!string.IsNullOrWhiteSpace(explicitConnectionString))
        {
            o.ConnectionString = explicitConnectionString;
        }
        else
        {
            // Use orchestration-aware resolution
            o.ConnectionString = resolver.ResolveConnectionString("redis", hints);
        }

        // Configure other Redis options
        var db = Koan.Core.Configuration.ReadFirst(_cfg, o.Database,
            $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.Database}",
            $"{Infrastructure.Constants.Configuration.Section_Sources_Default}:{Infrastructure.Constants.Configuration.Keys.Database}");
        o.Database = db;

        var def = Koan.Core.Configuration.ReadFirst(_cfg, o.DefaultPageSize,
            $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.DefaultPageSize}",
            $"{Infrastructure.Constants.Configuration.Section_Sources_Default}:{Infrastructure.Constants.Configuration.Keys.DefaultPageSize}");
        if (def > 0) o.DefaultPageSize = def;

        var max = Koan.Core.Configuration.ReadFirst(_cfg, o.MaxPageSize,
            $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.MaxPageSize}",
            $"{Infrastructure.Constants.Configuration.Section_Sources_Default}:{Infrastructure.Constants.Configuration.Keys.MaxPageSize}");
        if (max > 0) o.MaxPageSize = max;

        if (o.DefaultPageSize > o.MaxPageSize) o.DefaultPageSize = o.MaxPageSize;
    }

    /// <summary>
    /// Check for explicit connection strings from environment variables or multi-endpoint lists
    /// </summary>
    private string? CheckExplicitConnectionStrings()
    {
        // Check environment variables first
        var envConnectionString = Koan.Core.Configuration.ReadFirst(_cfg,
            Infrastructure.Constants.Discovery.EnvRedisUrl,
            Infrastructure.Constants.Discovery.EnvRedisConnectionString);
        if (!string.IsNullOrWhiteSpace(envConnectionString))
        {
            return envConnectionString;
        }

        // Multi-endpoint env list; pick the first that responds to a short ping
        var list = Environment.GetEnvironmentVariable(Infrastructure.Constants.Discovery.EnvRedisList);
        if (!string.IsNullOrWhiteSpace(list))
        {
            foreach (var part in list.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = part.Trim();
                if (string.IsNullOrWhiteSpace(candidate)) continue;
                if (TryRedisPing(candidate, TimeSpan.FromMilliseconds(250)))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    private static bool TryRedisPing(string configuration, TimeSpan timeout)
    {
        try
        {
            var options = ConfigurationOptions.Parse(configuration);
            options.ConnectTimeout = (int)timeout.TotalMilliseconds;
            options.SyncTimeout = (int)timeout.TotalMilliseconds;
            using var muxer = ConnectionMultiplexer.Connect(options);
            return muxer.IsConnected;
        }
        catch { return false; }
    }
}