using Koan.Core.Hosting.Bootstrap;

namespace Koan.Redis.Infrastructure;

internal static class RedisProvenanceItems
{
    internal static readonly ProvenanceItem ConnectionString = new(
        Constants.Configuration.StandardConnectionString,
        "Redis endpoint",
        "Shared Redis connection used by every Redis-backed Koan capability in this host.",
        MustSanitize: true,
        DefaultValue: "auto",
        DefaultConsumers:
        [
            "Koan.Redis.Connections.RedisConnectionProvider",
            "StackExchange.Redis.IConnectionMultiplexer"
        ]);
}
