using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Core.Hosting.App;
using Koan.Core.Hosting.Bootstrap;
using Koan.Flow.Attributes;
using Koan.Flow.Context;
using Koan.Flow.Core.Orchestration;
using Koan.Messaging;

namespace Koan.Flow.Core.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Flow.Core";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register core Flow services including interceptors
        services.AddKoanFlow();
        
        // Auto-register BackgroundService classes with FlowAdapterAttribute
        RegisterFlowAdapters(services);
        
        // NEW: Auto-discover and register orchestrators
        RegisterFlowOrchestrators(services);
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        
        // Count how many Flow adapters were discovered
        var adapterCount = DiscoverFlowAdapters().Count();
        report.AddSetting("FlowAdaptersDiscovered", adapterCount.ToString());
    }

    private void RegisterFlowAdapters(IServiceCollection services)
    {
        var adapters = DiscoverFlowAdapters().ToList();
        
        foreach (var adapterType in adapters)
        {
            var adapterAttr = adapterType.GetCustomAttribute<FlowAdapterAttribute>(inherit: true);
            if (adapterAttr == null) continue;
            
            // Register the adapter service itself
            services.AddSingleton(adapterType);
            
            // Register the context wrapper as the hosted service
            services.AddSingleton<IHostedService>(sp =>
            {
                var adapterService = (BackgroundService)sp.GetRequiredService(adapterType);
                var loggerType = typeof(ILogger<>).MakeGenericType(typeof(FlowAdapterContextService<>).MakeGenericType(adapterType));
                var logger = sp.GetRequiredService(loggerType);
                
                var contextServiceType = typeof(FlowAdapterContextService<>).MakeGenericType(adapterType);
                return (IHostedService)Activator.CreateInstance(contextServiceType, adapterService, logger)!;
            });
        }
    }

    private IEnumerable<Type> DiscoverFlowAdapters()
    {
        // Use cached assemblies instead of bespoke AppDomain scanning
        var assemblies = AssemblyCache.Instance.GetAllAssemblies();
        
        foreach (var assembly in assemblies)
        {
            Type?[] types;
            try 
            { 
                types = assembly.GetTypes(); 
            }
            catch (ReflectionTypeLoadException rtle) 
            { 
                types = rtle.Types; 
            }
            catch 
            { 
                continue; 
            }
            
            foreach (var type in types)
            {
                if (type == null || !type.IsClass || type.IsAbstract) continue;
                
                // Check if it's a BackgroundService
                if (!typeof(BackgroundService).IsAssignableFrom(type)) continue;
                
                // Check if it has FlowAdapterAttribute
                if (type.GetCustomAttribute<FlowAdapterAttribute>(inherit: true) == null) continue;
                
                yield return type;
            }
        }
    }

    private void RegisterFlowOrchestrators(IServiceCollection services)
    {
        var orchestrators = DiscoverFlowOrchestrators().ToList();
        
        // Only register orchestrators if user-defined ones are found
        // Do not automatically add DefaultFlowOrchestrator - orchestrators should be explicit
        
        // For now, we'll register one handler that finds the orchestrator dynamically
        // Later we can improve this to support multiple orchestrators
        foreach (var orchestratorType in orchestrators)
        {
            // Register the orchestrator service itself
            services.AddSingleton(orchestratorType);
            
            // ALSO register as IFlowOrchestrator interface for discovery
            services.AddSingleton<IFlowOrchestrator>(sp => (IFlowOrchestrator)sp.GetRequiredService(orchestratorType));
            
            // Register as hosted service
            services.AddSingleton<IHostedService>(sp => (IHostedService)sp.GetRequiredService(orchestratorType));
        }
        
        // Note: Orchestrator routing is now integrated into FlowMessagingInitializer
        // rather than competing string handlers
    }

    private IEnumerable<Type> DiscoverFlowOrchestrators()
    {
        // Use cached assemblies instead of bespoke AppDomain scanning
        var assemblies = AssemblyCache.Instance.GetAllAssemblies();
        
        foreach (var assembly in assemblies)
        {
            Type?[] types;
            try 
            { 
                types = assembly.GetTypes(); 
            }
            catch (ReflectionTypeLoadException rtle) 
            { 
                types = rtle.Types; 
            }
            catch 
            { 
                continue; 
            }
            
            foreach (var type in types)
            {
                if (type == null || !type.IsClass || type.IsAbstract) continue;
                
                // Skip the built-in DefaultFlowOrchestrator if user has defined their own
                if (type == typeof(DefaultFlowOrchestrator) && HasUserDefinedOrchestrator(types)) continue;
                
                // Check if it has FlowOrchestratorAttribute
                if (type.GetCustomAttribute<FlowOrchestratorAttribute>(inherit: true) == null) continue;
                
                // Ensure it implements IFlowOrchestrator or inherits from FlowOrchestratorBase
                if (!typeof(IFlowOrchestrator).IsAssignableFrom(type)) continue;
                
                yield return type;
            }
        }
    }

    private bool HasUserDefinedOrchestrator(Type?[] types)
    {
        return types.Any(t => t != null && 
                             t != typeof(DefaultFlowOrchestrator) && 
                             t.GetCustomAttribute<FlowOrchestratorAttribute>(inherit: true) != null &&
                             typeof(IFlowOrchestrator).IsAssignableFrom(t));
    }
}