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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MongoHealthContributor>());
    }

    public void Describe(SoraBootstrapReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        var o = new MongoOptions
        {
            ConnectionString = Sora.Core.Configuration.ReadFirst(cfg, MongoConstants.DefaultLocalUri,
                Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.ConnectionString,
                Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.AltConnectionString,
                Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.ConnectionStringsMongo,
                Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault),
            Database = Sora.Core.Configuration.ReadFirst(cfg, "sora",
                Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.Database,
                Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.AltDatabase)
        };
        // Resolve connection string similarly to configurator: fallback to ConnectionStrings:Default if unset
        var cs = o.ConnectionString;
        var csByName = Sora.Core.Configuration.Read(cfg, Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault, null);
        if (string.IsNullOrWhiteSpace(cs) && !string.IsNullOrWhiteSpace(csByName)) cs = csByName;
        if (string.IsNullOrWhiteSpace(cs))
        {
            var inContainer = Sora.Core.SoraEnv.InContainer;
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
        report.AddSetting(Sora.Data.Mongo.Infrastructure.Constants.Bootstrap.EnsureCreatedSupported, true.ToString());
        // Announce paging guardrails (decision 0044)
        var defSize = Sora.Core.Configuration.ReadFirst(cfg, o.DefaultPageSize,
            Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.DefaultPageSize,
            Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.AltDefaultPageSize);
        var maxSize = Sora.Core.Configuration.ReadFirst(cfg, o.MaxPageSize,
            Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.MaxPageSize,
            Sora.Data.Mongo.Infrastructure.Constants.Configuration.Keys.AltMaxPageSize);
        report.AddSetting(Sora.Data.Mongo.Infrastructure.Constants.Bootstrap.DefaultPageSize, defSize.ToString());
        report.AddSetting(Sora.Data.Mongo.Infrastructure.Constants.Bootstrap.MaxPageSize, maxSize.ToString());
    }
}
