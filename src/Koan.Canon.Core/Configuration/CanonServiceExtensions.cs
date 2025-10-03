using Koan.Canon.Core.Monitoring;
using Koan.Canon.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Canon.Core.Configuration;

public static class CanonServiceExtensions
{
    public static IServiceCollection AddCanonCore(
        this IServiceCollection services,
        Action<CanonOptimizationOptions>? configure = null)
    {
        CanonOptimizationConfiguration.AddCanonOptimizations(services, configure);
        services.TryAddSingleton<ICanonRuntime, InMemoryCanonRuntime>();
        services.TryAddSingleton<CanonPerformanceMonitor>();
        return services;
    }

    public static IServiceCollection AddCanonCoreFromEnvironment(
        this IServiceCollection services,
        string environmentPrefix = CanonConfigurationDefaults.EnvironmentPrefix)
    {
        CanonOptimizationConfiguration.AddCanonOptimizationsFromEnvironment(services, environmentPrefix);
        services.TryAddSingleton<ICanonRuntime, InMemoryCanonRuntime>();
        services.TryAddSingleton<CanonPerformanceMonitor>();
        return services;
    }

    public static CanonOptimizationOptions GetCanonOptimizationOptions(this IServiceProvider provider)
    {
        return provider.GetRequiredService<CanonOptimizationOptions>();
    }
}

public static class CanonConfigurationDefaults
{
    public const string EnvironmentPrefix = "Koan_CANON_";
}


