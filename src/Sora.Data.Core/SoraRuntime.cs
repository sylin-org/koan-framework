using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Instructions;

namespace Sora.Data.Core;

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
        var env = SoraEnv.EnvironmentName
                  ?? Sora.Core.Configuration.ReadFirst(null, Sora.Core.Infrastructure.Constants.Configuration.Env.AspNetCoreEnvironment, Sora.Core.Infrastructure.Constants.Configuration.Env.DotnetEnvironment)
                  ?? string.Empty;
        if (SoraEnv.IsProduction)
        {
            // In isolated/containerized environments, downgrade the noise level.
            var inContainer = SoraEnv.InContainer;
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
            var report = new SoraBootstrapReport();
            var envSvc = _sp.GetService<IHostEnvironment>();
            var cfg = _sp.GetService<IConfiguration>();
            var regs = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => { try { return a.GetTypes(); } catch { return Array.Empty<Type>(); } })
                .Where(t => !t.IsAbstract && typeof(ISoraAutoRegistrar).IsAssignableFrom(t))
                .Select(t => { try { return Activator.CreateInstance(t) as ISoraAutoRegistrar; } catch { return null; } })
                .Where(r => r is not null)
                .ToList();
            foreach (var r in regs!)
            {
                report.AddModule(r!.ModuleName, r.ModuleVersion);
                r.Describe(report, cfg!, envSvc!);
            }
            var show = !SoraEnv.IsProduction;
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
                .Select(t => new { Type = t, Iface = t.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntity<>)) })
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
                    if (repo is IInstructionExecutor<object>)
                    {
                        // We can’t cast to generic at compile-time; use reflection to call ExecuteAsync<bool>(new Instruction(RelationalInstructions.SchemaEnsureCreated))
                        var execIface = repo!.GetType().GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IInstructionExecutor<>));
                        if (execIface is not null)
                        {
                            var instrType = typeof(Instruction);
                            var instruction = Activator.CreateInstance(instrType, RelationalInstructions.SchemaEnsureCreated, null, null, null);
                            var method = execIface.GetMethod("ExecuteAsync");
                            var task = (Task)method!.Invoke(repo, new object?[] { instruction!, default(CancellationToken) })!;
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