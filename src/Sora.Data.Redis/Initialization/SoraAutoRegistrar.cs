using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Data.Abstractions;
using StackExchange.Redis;

namespace Sora.Data.Redis.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Redis";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddOptions<RedisOptions>().ValidateDataAnnotations();
        services.AddSingleton<IConfigureOptions<RedisOptions>, RedisOptionsConfigurator>();
    services.TryAddSingleton<Sora.Data.Abstractions.Naming.IStorageNameResolver, Sora.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        services.AddSingleton<IDataAdapterFactory, RedisAdapterFactory>();
        services.AddHealthContributor<RedisHealthContributor>();
        // Connection multiplexer singleton
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var cfg = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            var cs = cfg.ConnectionString;
            if (string.IsNullOrWhiteSpace(cs))
            {
                cs = Sora.Core.SoraEnv.InContainer ? Sora.Data.Redis.Infrastructure.Constants.Discovery.DefaultCompose : Sora.Data.Redis.Infrastructure.Constants.Discovery.DefaultLocal;
            }
            return ConnectionMultiplexer.Connect(cs);
        });
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var o = new RedisOptions();
        new RedisOptionsConfigurator(cfg).Configure(o);
        report.AddSetting("Database", o.Database.ToString());
        report.AddSetting("ConnectionString", o.ConnectionString ?? string.Empty, isSecret: true);
        report.AddSetting(Sora.Data.Redis.Infrastructure.Constants.Bootstrap.EnsureCreatedSupported, true.ToString());
        report.AddSetting(Sora.Data.Redis.Infrastructure.Constants.Bootstrap.DefaultPageSize, o.DefaultPageSize.ToString());
        report.AddSetting(Sora.Data.Redis.Infrastructure.Constants.Bootstrap.MaxPageSize, o.MaxPageSize.ToString());
    }
}
