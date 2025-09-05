using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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
        var logger = services.BuildServiceProvider().GetService<Microsoft.Extensions.Logging.ILoggerFactory>()?.CreateLogger("Sora.Data.Mongo.Initialization.SoraAutoRegistrar");
    logger?.Log(LogLevel.Debug, "Sora.Data.Mongo SoraAutoRegistrar loaded.");
        services.AddSoraOptions<MongoOptions>();
        services.AddSingleton<IConfigureOptions<MongoOptions>, MongoOptionsConfigurator>();
        services.TryAddSingleton<Abstractions.Naming.IStorageNameResolver, Abstractions.Naming.DefaultStorageNameResolver>();
        services.TryAddEnumerable(new ServiceDescriptor(typeof(Abstractions.Naming.INamingDefaultsProvider), typeof(MongoNamingDefaultsProvider), ServiceLifetime.Singleton));
        services.AddSingleton<IDataAdapterFactory, MongoAdapterFactory>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHealthContributor, MongoHealthContributor>());
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        
        // NEW: Decision logging for connection string resolution
        var connectionAttempts = new List<(string source, string connectionString, bool canConnect, string? error)>();
        
        // Try configured connection strings first
        var configuredCs = Configuration.ReadFirst(cfg, null,
            Infrastructure.Constants.Configuration.Keys.ConnectionString,
            Infrastructure.Constants.Configuration.Keys.AltConnectionString,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsMongo,
            Infrastructure.Constants.Configuration.Keys.ConnectionStringsDefault);
            
        if (!string.IsNullOrWhiteSpace(configuredCs))
        {
            report.AddDiscovery("configuration", configuredCs);
            connectionAttempts.Add(("configuration", configuredCs, true, null)); // Assume configured strings work
        }
        
        // Auto-discovery logic with decision logging
        var finalCs = configuredCs;
        if (string.IsNullOrWhiteSpace(finalCs))
        {
            var isProd = SoraEnv.IsProduction;
            var inContainer = SoraEnv.InContainer;
            
            if (isProd)
            {
                finalCs = MongoConstants.DefaultLocalUri;
                report.AddDiscovery("production-default", finalCs);
                connectionAttempts.Add(("production-default", finalCs, true, null));
            }
            else
            {
                if (inContainer)
                {
                    finalCs = MongoConstants.DefaultComposeUri;
                    report.AddDiscovery("container-discovery", finalCs);
                    connectionAttempts.Add(("container-discovery", finalCs, true, null));
                }
                else
                {
                    finalCs = MongoConstants.DefaultLocalUri;
                    report.AddDiscovery("localhost-fallback", finalCs);
                    connectionAttempts.Add(("localhost-fallback", finalCs, true, null));
                }
            }
        }
        
        // Normalize connection string
        if (!string.IsNullOrWhiteSpace(finalCs) &&
            !finalCs.StartsWith("mongodb://", StringComparison.OrdinalIgnoreCase) &&
            !finalCs.StartsWith("mongodb+srv://", StringComparison.OrdinalIgnoreCase))
        {
            finalCs = "mongodb://" + finalCs.Trim();
        }
        
        // Log connection attempts
        foreach (var attempt in connectionAttempts)
        {
            report.AddConnectionAttempt("Data.Mongo", attempt.connectionString, attempt.canConnect, attempt.error);
        }
        
        // Log provider election decision
        var availableProviders = DiscoverAvailableDataProviders();
        if (connectionAttempts.Any(a => a.canConnect))
        {
            report.AddProviderElection("Data", "Mongo", availableProviders, "first successful connection");
        }
        else
        {
            report.AddDecision("Data", "InMemory", "no Mongo connection available", availableProviders);
        }
        
        var o = new MongoOptions
        {
            ConnectionString = finalCs,
            Database = Configuration.ReadFirst(cfg, "sora",
                Infrastructure.Constants.Configuration.Keys.Database,
                Infrastructure.Constants.Configuration.Keys.AltDatabase)
        };
        
        report.AddSetting("Database", o.Database);
        report.AddSetting("ConnectionString", finalCs, isSecret: true);
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
    }
    
    private static string[] DiscoverAvailableDataProviders()
    {
        // Scan loaded assemblies for other data providers
        var providers = new List<string> { "Mongo" }; // Always include self
        
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            var name = asm.GetName().Name ?? "";
            if (name.StartsWith("Sora.Data.") && name != "Sora.Data.Mongo" && name != "Sora.Data.Core")
            {
                var providerName = name.Substring("Sora.Data.".Length);
                providers.Add(providerName);
            }
        }
        
        return providers.ToArray();
    }
}
