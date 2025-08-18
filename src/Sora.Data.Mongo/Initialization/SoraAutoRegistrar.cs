using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Data.Abstractions;

namespace Sora.Data.Mongo.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Mongo";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddOptions<MongoOptions>().ValidateDataAnnotations();
        services.AddSingleton<IConfigureOptions<MongoOptions>, MongoOptionsConfigurator>();
        services.TryAddSingleton<Sora.Data.Abstractions.Naming.IStorageNameResolver, Sora.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(Sora.Data.Abstractions.Naming.INamingDefaultsProvider), typeof(MongoNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.AddSingleton<IDataAdapterFactory, MongoAdapterFactory>();
        services.AddHealthContributor<MongoHealthContributor>();
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var o = new MongoOptions();
        cfg.GetSection("Sora:Data:Mongo").Bind(o);
        cfg.GetSection("Sora:Data:Sources:Default:mongo").Bind(o);
        // Resolve connection string similarly to configurator
        var cs = o.ConnectionString;
        var csByName = cfg.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(cs) && !string.IsNullOrWhiteSpace(csByName)) cs = csByName;
        report.AddSetting("Database", o.Database);
        report.AddSetting("ConnectionString", cs, isSecret: true);
    }
}
