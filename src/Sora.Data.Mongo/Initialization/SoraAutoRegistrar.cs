using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Core;
using Sora.Core.Modules;
using Sora.Data.Abstractions;

namespace Sora.Data.Mongo.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Data.Mongo";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        services.AddSoraOptions<MongoOptions>();
        services.AddSingleton<IConfigureOptions<MongoOptions>, MongoOptionsConfigurator>();
        services.TryAddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(Abstractions.Naming.INamingDefaultsProvider), typeof(MongoNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.AddSingleton<IDataAdapterFactory, MongoAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MongoHealthContributor>());
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var o = new MongoOptions
        {
            ConnectionString = Configuration.ReadFirst(cfg, MongoConstants.DefaultLocalUri,
                Infrastructure.Constants.Configuration.Keys.ConnectionString,
                Infrastructure.Constants.Configuration.Keys.AltConnectionString,
                Infrastructure.Constants.Configuration.Keys.ConnectionStringsMongo,
                Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault),
            Database = Configuration.ReadFirst(cfg, "sora",
                Infrastructure.Constants.Configuration.Keys.Database,
                Infrastructure.Constants.Configuration.Keys.AltDatabase)
        };
        // Resolve connection string similarly to configurator: fallback to ConnectionStrings:Default if unset
        var cs = o.ConnectionString;
        var csByName = Configuration.Read(cfg, Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault, null);
        if (string.IsNullOrWhiteSpace(cs) && !string.IsNullOrWhiteSpace(csByName)) cs = csByName;
        if (string.IsNullOrWhiteSpace(cs))
        {
            var inContainer = SoraEnv.InContainer;
            cs = inContainer ? MongoConstants.DefaultComposeUri : MongoConstants.DefaultLocalUri;
        }
        if (!string.IsNullOrWhiteSpace(cs) &&
            !cs.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) &&
            !cs.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
        {
            cs = "mongodb://" + cs.Trim();
        }
        report.AddSetting("Database", o.Database);
        report.AddSetting("ConnectionString", cs, isSecret: true);
        // Announce schema capability per acceptance criteria
        report.AddSetting(Infrastructure.Constants.Bootstrap.EnsureCreatedSupported, true.ToString());
        // Announce paging guardrails (decision 0044)
        var defSize = Configuration.ReadFirst(cfg, o.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);
        var maxSize = Configuration.ReadFirst(cfg, o.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);
        report.AddSetting(Infrastructure.Constants.Bootstrap.DefaultPageSize, defSize.ToString());
        report.AddSetting(Infrastructure.Constants.Bootstrap.MaxPageSize, maxSize.ToString());
        // Discovery visibility
        report.AddSetting("Discovery:EnvList", Infrastructure.Constants.Discovery.EnvList, isSecret: false);
        report.AddSetting("Discovery:DefaultLocal", MongoConstants.DefaultLocalUri, isSecret: false);
        report.AddSetting("Discovery:DefaultCompose", MongoConstants.DefaultComposeUri, isSecret: false);
    }
}
