using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace Koan.Data.Redis;

internal sealed class RedisOptionsConfigurator : IConfigureOptions<RedisOptions>
{
    private readonly IConfiguration? _cfg;
    public RedisOptionsConfigurator() { }
    public RedisOptionsConfigurator(IConfiguration cfg) { _cfg = cfg; }
    public void Configure(RedisOptions o)
    {
        var cs = Koan.Core.Configuration.ReadFirst(_cfg,
            Infrastructure.Constants.Discovery.EnvRedisUrl,
            Infrastructure.Constants.Discovery.EnvRedisConnectionString,
            $"{Infrastructure.Constants.Configuration.Section_Data}:{Infrastructure.Constants.Configuration.Keys.ConnectionString}",
            $"{Infrastructure.Constants.Configuration.Section_Sources_Default}:{Infrastructure.Constants.Configuration.Keys.ConnectionString}");
        if (!string.IsNullOrWhiteSpace(cs)) o.ConnectionString = cs;
        else
        {
            // Multi-endpoint env list; pick the first that responds to a short ping
            var list = Environment.GetEnvironmentVariable(Infrastructure.Constants.Discovery.EnvRedisList);
            if (!string.IsNullOrWhiteSpace(list))
            {
                foreach (var part in list.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var candidate = part.Trim();
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    if (TryRedisPing(candidate, TimeSpan.FromMilliseconds(250))) { o.ConnectionString = candidate; break; }
                }
            }
        }
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

        if (string.IsNullOrWhiteSpace(o.ConnectionString) || string.Equals(o.ConnectionString.Trim(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            // host/docker discovery pattern
            o.ConnectionString = Koan.Core.KoanEnv.InContainer ? Infrastructure.Constants.Discovery.DefaultCompose : Infrastructure.Constants.Discovery.DefaultLocal;
        }
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