using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Canon;

/// <summary>
/// ServiceCollection helpers for wiring the canon runtime using DI.
/// </summary>
public static class CanonRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the canon runtime and allows optional configurators to contribute pipelines.
    /// </summary>
    public static IServiceCollection AddCanonRuntime(this IServiceCollection services, Action<CanonRuntimeBuilder>? configure = null)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (configure is not null)
        {
            services.AddSingleton(new RuntimeConfigurationAction(configure));
        }

        services.TryAddSingleton<ICanonAuditSink, DefaultCanonAuditSink>();

        services.TryAddSingleton<CanonRuntimeConfiguration>(sp =>
        {
            var builder = new CanonRuntimeBuilder();
            var auditSink = sp.GetRequiredService<ICanonAuditSink>();
            builder.UseAuditSink(auditSink);
            CanonPipelineDiscovery.Configure(builder, sp);
            foreach (var contribution in sp.GetServices<RuntimeConfigurationAction>())
            {
                contribution.Configure(builder);
            }

            return builder.BuildConfiguration();
        });

        services.TryAddSingleton<ICanonRuntime>(sp =>
        {
            var configuration = sp.GetRequiredService<CanonRuntimeConfiguration>();
            return new CanonRuntime(configuration, sp);
        });

        return services;
    }

    private sealed class RuntimeConfigurationAction
    {
        private readonly Action<CanonRuntimeBuilder> _configure;

        public RuntimeConfigurationAction(Action<CanonRuntimeBuilder> configure)
        {
            _configure = configure ?? throw new ArgumentNullException(nameof(configure));
        }

        public void Configure(CanonRuntimeBuilder builder)
        {
            if (builder is null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            _configure(builder);
        }
    }
}
