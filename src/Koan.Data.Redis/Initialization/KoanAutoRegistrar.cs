using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Core;
using Koan.Core.Modules;
using Koan.Data.Abstractions;
using StackExchange.Redis;

namespace Koan.Data.Redis.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data.Redis";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        var logger = services.BuildServiceProvider().GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Koan.Data.Redis.Initialization.KoanAutoRegistrar");
    logger?.Log(LogLevel.Debug, "Koan.Data.Redis KoanAutoRegistrar loaded.");
        services.AddKoanOptions<RedisOptions>();
        services.AddSingleton<IConfigureOptions<RedisOptions>, RedisOptionsConfigurator>();
        services.TryAddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        services.AddSingleton<IDataAdapterFactory, RedisAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, RedisHealthContributor>());
        // Connection multiplexer singleton
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var cs = cfg.ConnectionString;
            if (string.IsNullOrWhiteSpace(cs))
            {
                cs = KoanEnv.InContainer ? Infrastructure.Constants.Discovery.DefaultCompose : Infrastructure.Constants.Discovery.DefaultLocal;
            }
            return ConnectionMultiplexer.Connect(cs);
        });
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var o = new RedisOptions();
        new RedisOptionsConfigurator(cfg).Configure(o);
        report.AddSetting("Database", o.Database.ToString());
        report.AddSetting("ConnectionString", o.ConnectionString ?? string.Empty, isSecret: true);
        report.AddSetting(Infrastructure.Constants.Bootstrap.EnsureCreatedSupported, true.ToString());
        report.AddSetting(Infrastructure.Constants.Bootstrap.DefaultPageSize, o.DefaultPageSize.ToString());
        report.AddSetting(Infrastructure.Constants.Bootstrap.MaxPageSize, o.MaxPageSize.ToString());
        // Discovery visibility
        report.AddSetting("Discovery:EnvList", Infrastructure.Constants.Discovery.EnvRedisList, isSecret: false);
        report.AddSetting("Discovery:DefaultLocal", Infrastructure.Constants.Discovery.DefaultLocal, isSecret: false);
        report.AddSetting("Discovery:DefaultCompose", Infrastructure.Constants.Discovery.DefaultCompose, isSecret: false);
    }
}
