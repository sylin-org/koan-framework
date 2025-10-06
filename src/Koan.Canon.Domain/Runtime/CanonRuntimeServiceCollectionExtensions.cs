using System;
using System.Collections.Generic;
using Koan.Canon.Domain.Audit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Canon.Domain.Runtime;

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
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ICanonRuntimeConfigurator>(new DelegateCanonRuntimeConfigurator(configure)));
        }

        services.TryAddSingleton<ICanonAuditSink, DefaultCanonAuditSink>();

        services.TryAddSingleton<CanonRuntimeConfiguration>(sp =>
        {
            var builder = new CanonRuntimeBuilder();
            var auditSink = sp.GetRequiredService<ICanonAuditSink>();
            builder.UseAuditSink(auditSink);
            foreach (var configurator in sp.GetServices<ICanonRuntimeConfigurator>())
            {
                configurator.Configure(builder);
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

    private sealed class DelegateCanonRuntimeConfigurator : ICanonRuntimeConfigurator
    {
        private readonly Action<CanonRuntimeBuilder> _configure;

        public DelegateCanonRuntimeConfigurator(Action<CanonRuntimeBuilder> configure)
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
