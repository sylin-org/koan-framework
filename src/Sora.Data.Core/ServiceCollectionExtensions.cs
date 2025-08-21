using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
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
        services.TryAddSingleton<Sora.Data.Core.Configuration.IDataConnectionResolver, Sora.Data.Core.Configuration.DefaultDataConnectionResolver>();
        // Provide a default storage name resolver so naming works even without adapter-specific registration (e.g., JSON adapter)
        services.TryAddSingleton<Sora.Data.Abstractions.Naming.IStorageNameResolver, Sora.Data.Abstractions.Naming.DefaultStorageNameResolver>();
        services.AddOptions<Sora.Data.Core.Options.DirectOptions>().BindConfiguration("Sora:Data:Direct");
    // Vector defaults now live in Sora.Data.Vector; apps should call AddSoraDataVector() to enable vector features.
        services.AddOptions<DataRuntimeOptions>();
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
        Sora.Core.SoraApp.Current = sp;
        sp.UseSora();
        sp.StartSora();
        return sp;
    }
}

internal sealed class SoraRuntime : ISoraRuntime
{
    private readonly IServiceProvider _sp;
    private readonly Microsoft.Extensions.Options.IOptions<DataRuntimeOptions> _options;
    public SoraRuntime(IServiceProvider sp, Microsoft.Extensions.Options.IOptions<DataRuntimeOptions> options)
    { _sp = sp; _options = options; }
    public void Discover()
    {
        // Touch IDataService to ensure factories are available
        _ = _sp.GetService<IDataService>();

        // Warn in production if discovery is running (explicit registration still wins)
        var env = Sora.Core.SoraEnv.EnvironmentName
              ?? Sora.Core.Configuration.ReadFirst(null, Sora.Core.Infrastructure.Constants.Configuration.Env.AspNetCoreEnvironment, Sora.Core.Infrastructure.Constants.Configuration.Env.DotnetEnvironment)
              ?? string.Empty;
        if (Sora.Core.SoraEnv.IsProduction)
        {
            // In isolated/containerized environments, downgrade the noise level.
            var inContainer = Sora.Core.SoraEnv.InContainer;
            try
            {
                if (inContainer)
                    Console.WriteLine("[Sora] INFO: Module discovery executed in Production (container). Explicit registrations override discovery; ensure this is intended.");
                else
                    Console.WriteLine("[Sora] WARNING: Module discovery executed in Production. Explicit registrations override discovery; ensure this is intended.");
            }
            catch { /* ignore */ }
        }

        // Bootstrap report: aggregate from ISoraAutoRegistrar implementations
        try
        {
            var report = new Sora.Core.SoraBootstrapReport();
            var envSvc = _sp.GetService<IHostEnvironment>();
            var cfg = _sp.GetService<IConfiguration>();
            var regs = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .Where(t => !t.IsAbstract && typeof(Sora.Core.ISoraAutoRegistrar).IsAssignableFrom(t))
                .Select(t => { try { return Activator.CreateInstance(t) as Sora.Core.ISoraAutoRegistrar; } catch { return null; } })
                .Where(r => r is not null)
                .ToList();
            foreach (var r in regs!)
            {
                report.AddModule(r!.ModuleName, r.ModuleVersion);
                r.Describe(report, cfg!, envSvc!);
            }
            var show = !Sora.Core.SoraEnv.IsProduction;
            var obs = _sp.GetService<Microsoft.Extensions.Options.IOptions<Sora.Core.Observability.ObservabilityOptions>>();
            if (!show)
                show = obs?.Value?.Enabled == true && obs.Value?.Traces?.Enabled == true; // signal that observability is on
            if (show)
            {
                try { Console.Write(report.ToString()); } catch { }
            }
        }
        catch { /* best-effort */ }
    }
    public void Start()
    {
        try
        {
            if (!_options.Value.EnsureSchemaOnStart) return;
            // Enumerate known IEntity<> types from loaded assemblies.
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            var entityTypes = assemblies
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); } catch { return Array.Empty<Type>(); }
                })
                .Where(t => t.IsClass && !t.IsAbstract)
                .Select(t => new { Type = t, Iface = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Sora.Data.Abstractions.IEntity<>)) })
                .Where(x => x.Iface is not null)
                .Select(x => (x.Type, Key: x.Iface!.GetGenericArguments()[0]))
                .ToList();

            using var scope = _sp.CreateScope();
            var data = scope.ServiceProvider.GetRequiredService<IDataService>();

            foreach (var (entityType, keyType) in entityTypes)
            {
                try
                {
                    // Resolve repository dynamically
                    var mi = typeof(IDataService).GetMethod(nameof(IDataService.GetRepository))!;
                    var gm = mi.MakeGenericMethod(entityType, keyType);
                    var repo = gm.Invoke(data, null);
                    if (repo is Sora.Data.Abstractions.Instructions.IInstructionExecutor<object>)
                    {
                        // We can’t cast to generic at compile-time; use reflection to call ExecuteAsync<bool>(new Instruction(RelationalInstructions.SchemaEnsureCreated))
                        var execIface = repo!.GetType().GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(Sora.Data.Abstractions.Instructions.IInstructionExecutor<>));
                        if (execIface is not null)
                        {
                            var instrType = typeof(Sora.Data.Abstractions.Instructions.Instruction);
                            var instruction = Activator.CreateInstance(instrType, Relational.RelationalInstructions.SchemaEnsureCreated, null, null, null);
                            var method = execIface.GetMethod("ExecuteAsync");
                            var task = (System.Threading.Tasks.Task)method!.Invoke(repo, new object?[] { instruction!, default(System.Threading.CancellationToken) })!;
                            task.GetAwaiter().GetResult();
                        }
                    }
                }
                catch { /* best-effort */ }
            }
        }
        catch { /* best-effort */ }
    }
}
