using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Koan.Orchestration;
using Koan.Orchestration.Models;

namespace Koan.Core.Adapters;

/// <summary>
/// Bridge between orchestration (KoanService) and runtime (KoanAdapter) concerns.
/// Provides discovery, metadata exchange, and lifecycle coordination between layers.
/// </summary>
public static class OrchestrationRuntimeBridge
{
    /// <summary>
    /// Discover adapters that have both orchestration and runtime capabilities
    /// </summary>
    public static IEnumerable<AdapterDiscoveryResult> DiscoverUnifiedAdapters(Assembly assembly)
    {
        var results = new List<AdapterDiscoveryResult>();

        foreach (var type in assembly.GetTypes())
        {
            if (!typeof(IKoanAdapter).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                continue;

            var orchestrationMetadata = GetOrchestrationMetadata(type);
            var runtimeMetadata = GetRuntimeMetadata(type);

            results.Add(new AdapterDiscoveryResult
            {
                AdapterType = type,
                OrchestrationMetadata = orchestrationMetadata,
                RuntimeMetadata = runtimeMetadata
            });
        }

        return results;
    }

    /// <summary>
    /// Create a unified service registration that supports both orchestration and runtime concerns
    /// </summary>
    public static IServiceCollection RegisterUnifiedAdapter<T>(
        IServiceCollection services,
        IConfiguration configuration,
        AdapterDiscoveryResult discoveryResult) where T : class, IKoanAdapter
    {
        // Register the adapter
        services.AddSingleton<T>();

        // Register as IKoanAdapter for discovery
        services.AddSingleton<IKoanAdapter>(sp => sp.GetRequiredService<T>());

        return services;
    }

    /// <summary>
    /// Extract orchestration configuration and apply it to runtime adapter configuration
    /// </summary>
    public static IConfiguration BridgeConfiguration(
        IConfiguration configuration,
        UnifiedServiceMetadata orchestrationContext,
        string adapterId)
    {
        var bridgedConfig = new ConfigurationBuilder()
            .AddConfiguration(configuration);

        // Add orchestration-aware configuration
        if (orchestrationContext.IsOrchestrationAware)
        {
            var orchestrationConfig = new Dictionary<string, string?>
            {
                [$"Koan:Services:{adapterId}:OrchestrationMode"] = "managed",
                [$"Koan:Services:{adapterId}:ServiceKind"] = orchestrationContext.ServiceKind.ToString()
            };

            bridgedConfig.AddInMemoryCollection(orchestrationConfig);
        }

        return bridgedConfig.Build();
    }

    /// <summary>
    /// Check if an adapter supports orchestration-aware initialization
    /// </summary>
    public static bool SupportsOrchestrationAwareInit(Type adapterType)
    {
        return adapterType.GetMethods()
            .Any(m => m.GetCustomAttribute<OrchestrationAwareAttribute>() != null);
    }

    /// <summary>
    /// Initialize adapter with orchestration context if supported
    /// </summary>
    public static async Task<bool> TryInitializeWithOrchestrationAsync(
        IKoanAdapter adapter,
        UnifiedServiceMetadata orchestrationContext,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        var adapterType = adapter.GetType();
        var orchestrationMethod = adapterType.GetMethods()
            .FirstOrDefault(m => m.GetCustomAttribute<OrchestrationAwareAttribute>() != null &&
                                m.Name.Contains("Orchestration"));

        if (orchestrationMethod == null)
            return false;

        try
        {
            var parameters = orchestrationMethod.GetParameters();
            var args = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                if (paramType == typeof(UnifiedServiceMetadata))
                {
                    args[i] = orchestrationContext;
                }
                else if (paramType == typeof(CancellationToken))
                {
                    args[i] = cancellationToken;
                }
                else
                {
                    args[i] = GetDefaultValue(paramType)!;
                }
            }

            var result = orchestrationMethod.Invoke(adapter, args);
            if (result is Task task)
            {
                await task;
            }

            logger.LogDebug("[{AdapterId}] Orchestration-aware initialization completed", adapter.AdapterId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "[{AdapterId}] Orchestration-aware initialization failed, falling back to standard initialization", adapter.AdapterId);
            return false;
        }
    }

    /// <summary>
    /// Create runtime lifecycle hooks based on orchestration metadata
    /// </summary>
    public static AdapterLifecycleHooks CreateLifecycleHooks(UnifiedServiceMetadata orchestrationContext)
    {
        return new AdapterLifecycleHooks
        {
            PreInitialize = async (adapter, ct) =>
            {
                if (orchestrationContext.HasCapability("container_managed"))
                {
                    // Wait for container readiness
                    await Task.Delay(1000, ct); // Simplified implementation
                }
            },
            PostInitialize = async (adapter, ct) =>
            {
                // Post-initialization hooks
                await Task.CompletedTask;
            }
        };
    }

    /// <summary>
    /// Get default value for a type during reflection-based construction
    /// </summary>
    public static object? GetDefaultValue(Type type)
    {
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private static AdapterOrchestrationMetadata GetOrchestrationMetadata(Type type)
    {
        // Extract orchestration metadata from attributes
        return new AdapterOrchestrationMetadata
        {
            ServiceType = ServiceType.Service, // Default
            SupportsContainerOrchestration = SupportsOrchestrationAwareInit(type)
        };
    }

    private static AdapterRuntimeMetadata GetRuntimeMetadata(Type type)
    {
        return new AdapterRuntimeMetadata
        {
            AdapterType = type,
            SupportsHealthChecks = typeof(IKoanAdapter).IsAssignableFrom(type)
        };
    }
}

/// <summary>
/// Result of adapter discovery that includes both orchestration and runtime metadata
/// </summary>
public class AdapterDiscoveryResult
{
    public required Type AdapterType { get; init; }
    public required AdapterOrchestrationMetadata OrchestrationMetadata { get; init; }
    public required AdapterRuntimeMetadata RuntimeMetadata { get; init; }
}

/// <summary>
/// Runtime-specific adapter metadata
/// </summary>
public class AdapterRuntimeMetadata
{
    public required Type AdapterType { get; init; }
    public bool SupportsHealthChecks { get; init; }
}

/// <summary>
/// Orchestration-specific adapter metadata
/// </summary>
public class AdapterOrchestrationMetadata
{
    public ServiceType ServiceType { get; init; }
    public bool SupportsContainerOrchestration { get; init; }
}

/// <summary>
/// Lifecycle hooks for adapter initialization and shutdown
/// </summary>
public class AdapterLifecycleHooks
{
    public Func<IKoanAdapter, CancellationToken, Task>? PreInitialize { get; init; }
    public Func<IKoanAdapter, CancellationToken, Task>? PostInitialize { get; init; }
    public Func<IKoanAdapter, CancellationToken, Task>? PreShutdown { get; init; }
    public Func<IKoanAdapter, CancellationToken, Task>? PostShutdown { get; init; }
}