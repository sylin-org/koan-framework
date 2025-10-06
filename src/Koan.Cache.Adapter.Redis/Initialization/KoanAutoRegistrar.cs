using Koan.Cache.Abstractions;
using Koan.Cache.Extensions;
using Koan.Core;
using Koan.Core.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Cache.Adapter.Redis.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Cache.Adapter.Redis";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddKoanCacheAdapter("redis");
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);

        var configuration = Configuration.Read(cfg, CacheConstants.Configuration.Redis.Configuration, "auto");
        var channel = Configuration.Read(cfg, CacheConstants.Configuration.Redis.ChannelName, "koan-cache");
        var prefix = Configuration.Read(cfg, CacheConstants.Configuration.Redis.KeyPrefix, "cache:");

        report.AddProviderElection("CacheStore", "redis", new[] { "memory", "redis", "custom" }, "Reference = redis adapter package");
        report.AddSetting("RedisConfiguration", configuration ?? "auto");
        report.AddSetting("ChannelName", channel ?? "koan-cache");
        report.AddSetting("KeyPrefix", prefix ?? "cache:");
    }
}
