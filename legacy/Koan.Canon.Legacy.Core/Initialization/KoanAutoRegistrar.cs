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
using Koan.Canon.Attributes;
using Koan.Canon.Context;
using Koan.Canon.Core.Orchestration;
using Koan.Messaging;

namespace Koan.Canon.Core.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Canon.Core";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Register core Canon services including interceptors
        services.AddKoanCanon();
        
        // Auto-register BackgroundService classes with CanonAdapterAttribute
        RegisterCanonAdapters(services);
        
        // NEW: Auto-discover and register orchestrators
        RegisterCanonOrchestrators(services);
    }

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
        
        // Count how many Canon adapters were discovered
        var adapterCount = DiscoverCanonAdapters().Count();
        report.AddSetting("CanonAdaptersDiscovered", adapterCount.ToString());
    }

    private void RegisterCanonAdapters(IServiceCollection services)
    {
        var adapters = DiscoverCanonAdapters().ToList();
        
        foreach (var adapterType in adapters)
        {
            var adapterAttr = adapterType.GetCustomAttribute<CanonAdapterAttribute>(inherit: true);
            if (adapterAttr == null) continue;
            
            // Register the adapter service itself
            services.AddSingleton(adapterType);
            
            // Register the context wrapper as the hosted service
            services.AddSingleton<IHostedService>(sp =>
            {
                var adapterService = (BackgroundService)sp.GetRequiredService(adapterType);
                var loggerType = typeof(ILogger<>).MakeGenericType(typeof(CanonAdapterContextService<>).MakeGenericType(adapterType));
                var logger = sp.GetRequiredService(loggerType);
                
                var contextServiceType = typeof(CanonAdapterContextService<>).MakeGenericType(adapterType);
                return (IHostedService)Activator.CreateInstance(contextServiceType, adapterService, logger)!;
            });
        }
    }

    private IEnumerable<Type> DiscoverCanonAdapters()
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
                
                // Check if it has CanonAdapterAttribute
                if (type.GetCustomAttribute<CanonAdapterAttribute>(inherit: true) == null) continue;
                
                yield return type;
            }
        }
    }

    private void RegisterCanonOrchestrators(IServiceCollection services)
    {
        var orchestrators = DiscoverCanonOrchestrators().ToList();
        
        // Only register orchestrators if user-defined ones are found
        // Do not automatically add DefaultCanonOrchestrator - orchestrators should be explicit
        
        // For now, we'll register one handler that finds the orchestrator dynamically
        // Later we can improve this to support multiple orchestrators
        foreach (var orchestratorType in orchestrators)
        {
            // Register the orchestrator service itself
            services.AddSingleton(orchestratorType);
            
            // ALSO register as ICanonOrchestrator interface for discovery
            services.AddSingleton<ICanonOrchestrator>(sp => (ICanonOrchestrator)sp.GetRequiredService(orchestratorType));
            
            // Register as hosted service
            services.AddSingleton<IHostedService>(sp => (IHostedService)sp.GetRequiredService(orchestratorType));
        }
        
        // Note: Orchestrator routing is now integrated into CanonMessagingInitializer
        // rather than competing string handlers
    }

    private IEnumerable<Type> DiscoverCanonOrchestrators()
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
                
                // Skip the built-in DefaultCanonOrchestrator if user has defined their own
                if (type == typeof(DefaultCanonOrchestrator) && HasUserDefinedOrchestrator(types)) continue;
                
                // Check if it has CanonOrchestratorAttribute
                if (type.GetCustomAttribute<CanonOrchestratorAttribute>(inherit: true) == null) continue;
                
                // Ensure it implements ICanonOrchestrator or inherits from CanonOrchestratorBase
                if (!typeof(ICanonOrchestrator).IsAssignableFrom(type)) continue;
                
                yield return type;
            }
        }
    }

    private bool HasUserDefinedOrchestrator(Type?[] types)
    {
        return types.Any(t => t != null && 
                             t != typeof(DefaultCanonOrchestrator) && 
                             t.GetCustomAttribute<CanonOrchestratorAttribute>(inherit: true) != null &&
                             typeof(ICanonOrchestrator).IsAssignableFrom(t));
    }
}

