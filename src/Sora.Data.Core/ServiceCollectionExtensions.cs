using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;
using Sora.Core.Modules;
using Sora.Data.Abstractions;

namespace Sora.Data.Core;

public static class ServiceCollectionExtensions
{
    // High-level bootstrap: AddSora()
    public static IServiceCollection AddSora(this IServiceCollection services)
    {
        services.AddSoraCore();
        var svc = services.AddSoraDataCore();
        // If Sora.Data.Direct is referenced, auto-register it (no hard reference from Core)
        try
        {
            var directReg = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .FirstOrDefault(t => t.IsSealed && t.IsAbstract == false && t.Name == "DirectRegistration" && t.Namespace == "Sora.Data.Direct");
            var mi = directReg?.GetMethod("AddSoraDataDirect", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            mi?.Invoke(null, new object?[] { services });
        }
        catch { /* optional */ }
        return svc;
    }

    public static IServiceCollection AddSoraDataCore(this IServiceCollection services)
    {
        // Initialize modules (adapters, etc.) that opt-in via ISoraInitializer
        SoraInitialization.InitializeModules(services);
        services.TryAddSingleton<Configuration.IDataConnectionResolver, Configuration.DefaultDataConnectionResolver>();
        // Provide a default storage name resolver so naming works even without adapter-specific registration (e.g., JSON adapter)
        services.TryAddSingleton<Sora.Data.Abstractions.Naming.IStorageNameResolver, Sora.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        services.AddSoraOptions<Options.DirectOptions>("Sora:Data:Direct");
        // Vector defaults now live in Sora.Data.Vector; apps should call AddSoraDataVector() to enable vector features.
        services.AddSoraOptions<DataRuntimeOptions>();
        services.AddSingleton<IAggregateIdentityManager, AggregateIdentityManager>();
        services.AddSingleton<IDataService, DataService>();
        services.AddSingleton<IDataDiagnostics, DataDiagnostics>();
        services.AddSingleton<ISoraRuntime, SoraRuntime>();
        // Decorate repositories registered as IDataRepository<,>
        services.TryDecorate(typeof(IDataRepository<,>), typeof(RepositoryFacade<,>));
        return services;
    }

    // One-liner startup: builds provider, runs discovery, starts runtime
    public static IServiceProvider StartSora(this IServiceCollection services)
    {
        // Avoid duplicate registration if already configured
        if (!services.Any(d => d.ServiceType == typeof(ISoraRuntime)))
            services.AddSora();

        // Provide a default IConfiguration only if the host hasn't already registered one
        if (!services.Any(d => d.ServiceType == typeof(IConfiguration)))
        {
            var cfg = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
                .AddEnvironmentVariables()
                .Build();
            services.AddSingleton<IConfiguration>(cfg);
        }
        var sp = services.BuildServiceProvider();
        SoraApp.Current = sp;
        sp.UseSora();
        sp.StartSora();
        return sp;
    }
}