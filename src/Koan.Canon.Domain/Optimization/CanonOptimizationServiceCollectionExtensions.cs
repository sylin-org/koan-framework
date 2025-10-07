using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Canon.Domain.Optimization;

public static class CanonOptimizationServiceCollectionExtensions
{
    public static IServiceCollection AddCanonOptimizations(this IServiceCollection services, Action<CanonOptimizationOptions>? configure = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.TryAddSingleton(provider =>
        {
            var options = CanonOptimizationPresets.Development();
            configure?.Invoke(options);
            return options;
        });

        services.TryAddSingleton<AdaptiveBatchProcessor>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<AdaptiveBatchProcessor>>();
            var options = provider.GetRequiredService<CanonOptimizationOptions>();
            return new AdaptiveBatchProcessor(logger, options);
        });

        services.TryAddSingleton<CanonPerformanceMonitor>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<CanonPerformanceMonitor>>();
            var options = provider.GetRequiredService<CanonOptimizationOptions>();
            return new CanonPerformanceMonitor(logger, options);
        });

        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, CanonPerformanceMonitoringHostedService>());

        return services;
    }

    public static IServiceCollection AddCanonOptimizationsForProduction(this IServiceCollection services)
    {
        return services.AddCanonOptimizations(options =>
        {
            var preset = CanonOptimizationPresets.Production();
            options.Features = preset.Features;
            options.Performance = preset.Performance;
            options.Monitoring = preset.Monitoring;
        });
    }

    public static IServiceCollection AddCanonOptimizationsForDevelopment(this IServiceCollection services)
    {
        return services.AddCanonOptimizations(options =>
        {
            var preset = CanonOptimizationPresets.Development();
            options.Features = preset.Features;
            options.Performance = preset.Performance;
            options.Monitoring = preset.Monitoring;
        });
    }
}
