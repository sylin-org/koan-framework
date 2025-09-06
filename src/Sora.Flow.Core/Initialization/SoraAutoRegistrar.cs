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
        // Check if this assembly is marked as a Flow orchestrator
        var orchestratorAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(asm => asm.GetCustomAttribute<FlowOrchestratorAttribute>() != null)
            .ToList();
        
        var isOrchestrator = orchestratorAssemblies.Any();
        
        if (isOrchestrator)
        {
            // ✨ Auto-start Flow runtime for orchestrators - zero config! ✨
            services.AddSoraFlow();
            
            // ✨ CRITICAL: Register message handlers IMMEDIATELY so RabbitMqConsumerAutoStarter detects them! ✨
            // This must happen during Initialize(), not via ISoraInitializer which runs later
            RegisterOrchestratorHandlers(services, orchestratorAssemblies);
        }
        else
        {
            // Producer-only adapters: just install naming for consistent storage
            services.AddSoraFlowNaming();
        }

        // Provide identity stamper for server-side stamping (safe in producers)
        services.TryAddSingleton<IFlowIdentityStamper, FlowIdentityStamper>();

        // Auto-register adapter publishers: BackgroundService types annotated with [FlowAdapter]
        // Config gates: Sora:Flow:Adapters:AutoStart (bool), Include (string[] "system:adapter"), Exclude (string[])
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ISoraInitializer>(sp => new AdapterScannerInitializer()));
    }
    
    private void RegisterOrchestratorHandlers(IServiceCollection services, List<Assembly> orchestratorAssemblies)
    {
        foreach (var assembly in orchestratorAssemblies)
        {
            var flowTypes = DiscoverFlowTypesInAssembly(assembly);
            try
            {
                Console.WriteLine($"[Flow.Auto][Discover] Assembly={assembly.GetName().Name} FlowTypes={flowTypes.Count}");
                foreach (var ft in flowTypes)
                    Console.WriteLine($"[Flow.Auto][Discover]  -> {ft.FullName}");
            }
            catch { }
            
            foreach (var flowType in flowTypes)
            {
                RegisterFlowHandler(services, flowType);
            }

            // Also register handler for FlowCommandMessage (named commands)
            RegisterCommandHandler(services);
        }
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

    private void RegisterFlowHandler(IServiceCollection services, Type flowType)
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

    private void RegisterEntityHandler(IServiceCollection services, Type entityType)
    {
        // Use a much simpler approach: register a generic handler factory
        // that dynamically creates handlers for each discovered Flow entity type
        
        var registerMethod = typeof(SoraAutoRegistrar)
            .GetMethod(nameof(RegisterEntityHandlerGeneric), BindingFlags.NonPublic | BindingFlags.Instance)
            ?.MakeGenericMethod(entityType);
            
        registerMethod?.Invoke(this, new object[] { services });
    }

    private void RegisterValueObjectHandler(IServiceCollection services, Type valueObjectType)
    {
        var registerMethod = typeof(SoraAutoRegistrar)
            .GetMethod(nameof(RegisterValueObjectHandlerGeneric), BindingFlags.NonPublic | BindingFlags.Instance)
            ?.MakeGenericMethod(valueObjectType);
            
        registerMethod?.Invoke(this, new object[] { services });
    }

    private void RegisterEntityHandlerGeneric<T>(IServiceCollection services) 
        where T : Model.FlowEntity<T>, new()
    {
    // Register handler that writes directly to Flow intake (targets only FlowTargetedMessage<T>)
    Console.WriteLine($"[Flow.Auto][Entity] Register targeted handler for {typeof(T).FullName}");
        services.On<FlowTargetedMessage<T>>(async msg =>
        {
            Console.WriteLine($"[Flow.Auto][Entity] Handler INVOKED for {typeof(T).Name} Id={msg.Entity?.GetType().GetProperty("Id")?.GetValue(msg.Entity)}");
            try
            {
                var sp = Sora.Core.Hosting.App.AppHost.Current;
                var sender = sp?.GetService<Sora.Flow.Sending.IFlowSender>();
                if (sender is null)
                {
                    // Fallback: previous behavior (will re-create backlog but preserves functionality if sender missing)
                    await Sora.Messaging.MessagingExtensions.Send(msg.Entity!);
                    return;
                }

                var bag = ToBag(msg.Entity!);
                var item = Sora.Flow.Sending.FlowSendPlainItem.Of<T>(bag, sourceId: "orchestrator", occurredAt: DateTimeOffset.UtcNow);
                Console.WriteLine($"[Flow.Auto][Entity] Intake targeted {typeof(T).Name} Id={msg.Entity?.GetType().GetProperty("Id")?.GetValue(msg.Entity)}");
                await sender.SendAsync(new[] { item }, message: msg, hostType: typeof(T));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️  Flow orchestrator failed to intake {typeof(T).Name}: {ex.Message}");
                throw; // Re-throw for messaging system error handling
            }
        });
    // Legacy plain entity consumption removed intentionally: all producers must emit FlowTargetedMessage<T>.
    }

    private void RegisterValueObjectHandlerGeneric<T>(IServiceCollection services) 
        where T : Model.FlowValueObject<T>, new()
    {
    Console.WriteLine($"[Flow.Auto][ValueObject] Register targeted handler for {typeof(T).FullName}");
    services.On<FlowTargetedMessage<T>>(async msg =>
        {
            Console.WriteLine($"[Flow.Auto][ValueObject] Handler INVOKED for {typeof(T).Name} Id={msg.Entity?.GetType().GetProperty("Id")?.GetValue(msg.Entity)}");
            try
            {
                var sp = Sora.Core.Hosting.App.AppHost.Current;
                var sender = sp?.GetService<Sora.Flow.Sending.IFlowSender>();
                if (sender is null)
                {
                    await Sora.Messaging.MessagingExtensions.Send(msg.Entity!);
                    return;
                }

                var bag = ToBag(msg.Entity!);
                var item = Sora.Flow.Sending.FlowSendPlainItem.Of<T>(bag, sourceId: "orchestrator", occurredAt: DateTimeOffset.UtcNow);
                Console.WriteLine($"[Flow.Auto][ValueObject] Intake targeted {typeof(T).Name} Id={msg.Entity?.GetType().GetProperty("Id")?.GetValue(msg.Entity)}");
                await sender.SendAsync(new[] { item }, message: msg, hostType: typeof(T));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"⚠️  Flow orchestrator failed to intake {typeof(T).Name}: {ex.Message}");
                throw;
            }
        });
    // Legacy plain value object consumption removed intentionally: all producers must emit FlowTargetedMessage<T>.
    }

    private static System.Collections.Generic.IDictionary<string, object?> ToBag(object entity)
    {
        var dict = new System.Collections.Generic.Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (entity is null) return dict;
        try
        {
            var props = entity.GetType().GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
            foreach (var p in props)
            {
                if (!p.CanRead) continue;
                var val = p.GetValue(entity);
                // Avoid deep object graphs / navigation properties; include primitives, strings, decimals, DateTimes, GUIDs, enums.
                if (val is null || IsSimple(val.GetType()))
                {
                    dict[p.Name] = val;
                }
            }
        }
        catch { }
        return dict;
    }

    private static bool IsSimple(Type t)
    {
        if (t.IsPrimitive || t.IsEnum) return true;
        return t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(DateTimeOffset) || t == typeof(Guid) || t == typeof(TimeSpan);
    }

    private void RegisterCommandHandler(IServiceCollection services)
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

    // OrchestratorScannerInitializer removed - handlers are now registered directly in Initialize()
    // to ensure they're available when RabbitMqConsumerAutoStarter checks for IMessageHandler<T> services
    
    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        var opts = cfg.GetSection("Sora:Flow").Get<FlowOptions>() ?? new FlowOptions();
        report.AddModule(ModuleName, ModuleVersion);
        
        // NEW: Decision logging for Flow runtime selection
        var availableRuntimes = DiscoverAvailableFlowRuntimes();
        var selectedRuntime = "InMemory"; // Default
        var reason = "default runtime";
        
        if (SoraEnv.InContainer)
        {
            reason = "container environment detected";
        }
        
        // Check if Dapr is available (example of runtime election)
        if (availableRuntimes.Contains("Dapr"))
        {
            // In a real scenario, we might have logic to prefer Dapr in certain conditions
            // For now, stick with InMemory but show the decision
            report.AddDecision("Flow.Runtime", selectedRuntime, reason, availableRuntimes);
        }
        else
        {
            report.AddProviderElection("Flow.Runtime", selectedRuntime, availableRuntimes, reason);
        }
        
        // NEW: Discovery logging for Flow model types
        var discoveredModels = DiscoverFlowModels();
        if (discoveredModels.Any())
        {
            report.AddDiscovery("flow-models", $"{discoveredModels.Count} FlowEntity/FlowValueObject types found");
            foreach (var model in discoveredModels.Take(3)) // Show first few
            {
                report.AddDiscovery("model-discovered", model);
            }
            if (discoveredModels.Count > 3)
            {
                report.AddNote($"and {discoveredModels.Count - 3} more Flow models");
            }
        }
        
        // NEW: Worker configuration decisions
        var workerDecisions = new[]
        {
            ("projection", "enabled", "canonical view generation required"),
            ("association", "enabled", "entity key resolution required"),
            ("purge", opts.PurgeEnabled ? "enabled" : "disabled", opts.PurgeEnabled ? "cleanup configured" : "retain all data")
        };
        
        foreach (var (worker, status, reasoning) in workerDecisions)
        {
            report.AddDecision($"Flow.Worker.{worker}", status, reasoning);
        }
        
        report.AddSetting("runtime", selectedRuntime);
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
    
    private static string[] DiscoverAvailableFlowRuntimes()
    {
        var runtimes = new List<string> { "InMemory" }; // Always available
        
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            var name = asm.GetName().Name ?? "";
            if (name.StartsWith("Sora.Flow.Runtime."))
            {
                var runtimeName = name.Substring("Sora.Flow.Runtime.".Length);
                runtimes.Add(runtimeName);
            }
        }
        
        return runtimes.ToArray();
    }
    
    private static List<string> DiscoverFlowModels()
    {
        var models = new List<string>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        
        foreach (var asm in assemblies)
        {
            Type[] types;
            try { types = asm.GetTypes(); }
            catch { continue; }
            
            foreach (var t in types)
            {
                if (t.IsAbstract || !t.IsClass) continue;
                var baseType = t.BaseType;
                if (baseType?.IsGenericType != true) continue;
                
                var genericDef = baseType.GetGenericTypeDefinition();
                if (genericDef == typeof(Model.FlowEntity<>) || genericDef == typeof(Model.FlowValueObject<>))
                {
                    models.Add(t.Name);
                }
            }
        }
        
        return models;
    }
}
