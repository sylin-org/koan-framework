using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Flow.Options;
using Sora.Flow.Sending;
using Sora.Flow.Attributes;
using Sora.Messaging;

namespace Sora.Flow.Initialization;

public sealed class SoraAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Flow.Core";
    public string? ModuleVersion => typeof(SoraAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Do not auto-start the Flow runtime in every process.
        // Producer-only adapters reference Sora.Flow.Core for types/attributes but must not run background workers.
        // Orchestrators/APIs should call services.AddSoraFlow() explicitly in Program.cs.
        // Safe default here: install naming only so storage naming remains consistent if used.
        services.AddSoraFlowNaming();

        // Provide identity stamper for server-side stamping (safe in producers)
        services.TryAddSingleton<IFlowIdentityStamper, FlowIdentityStamper>();

        // Auto-register adapter publishers: BackgroundService types annotated with [FlowAdapter]
        // Config gates: Sora:Flow:Adapters:AutoStart (bool), Include (string[] "system:adapter"), Exclude (string[])
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISoraInitializer>(sp => new AdapterScannerInitializer()));

        // ✨ BEAUTIFUL ORCHESTRATOR AUTO-REGISTRATION ✨
        // Auto-register Flow message handlers for assemblies marked with [FlowOrchestrator]
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISoraInitializer>(sp => new OrchestratorScannerInitializer()));
    }

    private sealed class AdapterScannerInitializer : ISoraInitializer
    {
        public void Initialize(IServiceCollection services)
        {
            // Resolve configuration if available via existing DI registrations during StartSora/AddSora pipelines
            IConfiguration? cfg = null;
            try
            {
                var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IConfiguration));
                cfg = existing?.ImplementationInstance as IConfiguration;
            }
            catch { }

            bool inContainer = SoraEnv.InContainer;
            bool autoStart = inContainer; // default: on in containers, off elsewhere
            string[] include = Array.Empty<string>();
            string[] exclude = Array.Empty<string>();
            try
            {
                if (cfg is not null)
                {
                    autoStart = cfg.GetValue<bool?>("Sora:Flow:Adapters:AutoStart") ?? autoStart;
                    include = cfg.GetSection("Sora:Flow:Adapters:Include").Get<string[]>() ?? include;
                    exclude = cfg.GetSection("Sora:Flow:Adapters:Exclude").Get<string[]>() ?? exclude;
                }
            }
            catch { }

            if (!autoStart) return;

            bool Matches(string sys, string adp)
            {
                var code = $"{sys}:{adp}";
                if (exclude.Any(x => string.Equals(x, code, StringComparison.OrdinalIgnoreCase))) return false;
                if (include.Length == 0) return true;
                return include.Any(x => string.Equals(x, code, StringComparison.OrdinalIgnoreCase));
            }

            foreach (var (t, attr) in DiscoverAdapterHostedServices())
            {
                if (!Matches(attr.System, attr.Adapter)) continue;
                services.AddSingleton(typeof(IHostedService), t);
            }
        }

        private static IEnumerable<(Type Type, FlowAdapterAttribute Attr)> DiscoverAdapterHostedServices()
        {
            var results = new List<(Type, FlowAdapterAttribute)>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var asm in assemblies)
            {
                Type?[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { continue; }
                foreach (var t in types)
                {
                    if (t is null || t.IsAbstract) continue;
                    if (!typeof(BackgroundService).IsAssignableFrom(t)) continue;
                    var attr = t.GetCustomAttribute<FlowAdapterAttribute>(inherit: true);
                    if (attr is null) continue;
                    results.Add((t, attr));
                }
            }
            return results;
        }
    }

    /// <summary>
    /// ✨ BEAUTIFUL ORCHESTRATOR AUTO-REGISTRATION ✨
    /// Scans for assemblies marked with [FlowOrchestrator] and auto-registers message handlers
    /// that bridge Sora.Messaging → Sora.Flow intake pipeline.
    /// </summary>
    private sealed class OrchestratorScannerInitializer : ISoraInitializer
    {
        public void Initialize(IServiceCollection services)
        {
            var orchestratorAssemblies = DiscoverOrchestratorAssemblies();
            
            foreach (var assembly in orchestratorAssemblies)
            {
                var flowTypes = DiscoverFlowTypesInAssembly(assembly);
                
                foreach (var flowType in flowTypes)
                {
                    RegisterFlowHandler(services, flowType);
                }

                // Also register handler for FlowCommandMessage (named commands)
                RegisterCommandHandler(services);
            }
        }

        private static List<Assembly> DiscoverOrchestratorAssemblies()
        {
            var result = new List<Assembly>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            
            foreach (var asm in assemblies)
            {
                try
                {
                    var attr = asm.GetCustomAttribute<FlowOrchestratorAttribute>();
                    if (attr is not null)
                        result.Add(asm);
                }
                catch
                {
                    // Skip assemblies that can't be examined
                }
            }
            
            return result;
        }

        private static List<Type> DiscoverFlowTypesInAssembly(Assembly assembly)
        {
            var result = new List<Type>();
            
            try
            {
                Type?[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
                catch { return result; }
                
                foreach (var t in types)
                {
                    if (t is null || !t.IsClass || t.IsAbstract) continue;
                    
                    // Check for FlowIgnore attribute to opt out
                    if (t.GetCustomAttribute<FlowIgnoreAttribute>() is not null) continue;
                    
                    var bt = t.BaseType;
                    if (bt is null || !bt.IsGenericType) continue;
                    
                    var def = bt.GetGenericTypeDefinition();
                    if (def == typeof(Model.FlowEntity<>) || def == typeof(Model.FlowValueObject<>))
                        result.Add(t);
                }
            }
            catch
            {
                // Skip assemblies that can't be examined
            }
            
            return result;
        }

        private static void RegisterFlowHandler(IServiceCollection services, Type flowType)
        {
            var baseType = flowType.BaseType;
            if (baseType?.IsGenericType != true) return;
            
            var genericDef = baseType.GetGenericTypeDefinition();
            
            if (genericDef == typeof(Model.FlowEntity<>))
            {
                // Register handler for FlowEntity - receives targeted messages and routes to Flow intake
                RegisterEntityHandler(services, flowType);
            }
            else if (genericDef == typeof(Model.FlowValueObject<>))
            {
                // Register handler for FlowValueObject - needs special handling
                RegisterValueObjectHandler(services, flowType);
            }
        }

        private static void RegisterEntityHandler(IServiceCollection services, Type entityType)
        {
            // Use a much simpler approach: register a generic handler factory
            // that dynamically creates handlers for each discovered Flow entity type
            
            var registerMethod = typeof(OrchestratorScannerInitializer)
                .GetMethod(nameof(RegisterEntityHandlerGeneric), BindingFlags.NonPublic | BindingFlags.Static)
                ?.MakeGenericMethod(entityType);
                
            registerMethod?.Invoke(null, new object[] { services });
        }

        private static void RegisterValueObjectHandler(IServiceCollection services, Type valueObjectType)
        {
            var registerMethod = typeof(OrchestratorScannerInitializer)
                .GetMethod(nameof(RegisterValueObjectHandlerGeneric), BindingFlags.NonPublic | BindingFlags.Static)
                ?.MakeGenericMethod(valueObjectType);
                
            registerMethod?.Invoke(null, new object[] { services });
        }

        private static void RegisterEntityHandlerGeneric<T>(IServiceCollection services) 
            where T : Model.FlowEntity<T>, new()
        {
            // Clean, simple registration using Sora.Messaging extensions
            services.On<FlowTargetedMessage<T>>(async (msg, ct) =>
            {
                try
                {
                    // Route the entity to Flow intake automatically
                    await msg.Entity.SendToFlowIntake(ct: ct);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️  Flow orchestrator failed to route {typeof(T).Name}: {ex.Message}");
                    throw; // Re-throw for messaging system error handling
                }
            });
        }

        private static void RegisterValueObjectHandlerGeneric<T>(IServiceCollection services) 
            where T : Model.FlowValueObject<T>, new()
        {
            services.On<FlowTargetedMessage<T>>(async (msg, ct) =>
            {
                try
                {
                    // Route the value object to Flow intake automatically  
                    await msg.Entity.SendToFlowIntake(ct: ct);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️  Flow orchestrator failed to route {typeof(T).Name}: {ex.Message}");
                    throw;
                }
            });
        }

        private static void RegisterCommandHandler(IServiceCollection services)
        {
            // Register a generic handler for FlowCommandMessage that logs or processes commands
            // This is a placeholder - real orchestrators would override specific command handlers
            services.On<FlowCommandMessage>(async msg =>
            {
                // For now, just log that we received a command
                // Real orchestrators would implement specific command handling logic
                System.Diagnostics.Debug.WriteLine($"Flow orchestrator received command: {msg.Command}");
                
                // TODO: Implement command routing/processing logic
                // This could route to specific handlers based on msg.Command
                await Task.CompletedTask;
            });
        }
    }

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        var opts = cfg.GetSection("Sora:Flow").Get<FlowOptions>() ?? new FlowOptions();
    report.AddModule(ModuleName, ModuleVersion);
    report.AddSetting("runtime", "InMemory");
    report.AddSetting("batch", opts.BatchSize.ToString());
    report.AddSetting("concurrency.Standardize", opts.StandardizeConcurrency.ToString());
    report.AddSetting("concurrency.Key", opts.KeyConcurrency.ToString());
    report.AddSetting("concurrency.Associate", opts.AssociateConcurrency.ToString());
    report.AddSetting("concurrency.Project", opts.ProjectConcurrency.ToString());
    report.AddSetting("ttl.Intake", opts.IntakeTtl.ToString());
    report.AddSetting("ttl.Standardized", opts.StandardizedTtl.ToString());
    report.AddSetting("ttl.Keyed", opts.KeyedTtl.ToString());
    report.AddSetting("ttl.ProjectionTask", opts.ProjectionTaskTtl.ToString());
    report.AddSetting("ttl.RejectionReport", opts.RejectionReportTtl.ToString());
    report.AddSetting("purge.Enabled", opts.PurgeEnabled.ToString().ToLowerInvariant());
    report.AddSetting("purge.Interval", opts.PurgeInterval.ToString());
    report.AddSetting("dlq", opts.DeadLetterEnabled.ToString().ToLowerInvariant());
    report.AddSetting("defaultView", opts.DefaultViewName);
    if (opts.AggregationTags?.Length > 0)
        report.AddSetting("aggregation.tags", string.Join(",", opts.AggregationTags));
    }
}
