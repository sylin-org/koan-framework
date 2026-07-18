using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Koan.Canon;

/// <summary>
/// ServiceCollection helpers for wiring the canon runtime using DI.
/// </summary>
internal static class CanonRuntimeServiceCollectionExtensions
{
    /// <summary>
    /// Registers the canon runtime and allows optional configurators to contribute pipelines.
    /// </summary>
    internal static IServiceCollection AddCanonRuntime(this IServiceCollection services)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        services.TryAddSingleton<ICanonPersistence, DefaultCanonPersistence>();
        services.TryAddSingleton<ICanonAuditSink, DefaultCanonAuditSink>();

        services.TryAddSingleton<CanonRuntimeConfiguration>(sp =>
        {
            var builder = new CanonRuntimeBuilder();
            var persistence = sp.GetRequiredService<ICanonPersistence>();
            var auditSink = sp.GetRequiredService<ICanonAuditSink>();
            builder.UsePersistence(persistence);
            builder.UseAuditSink(auditSink);
            CanonCompositionCompiler.Configure(
                builder,
                sp,
                sp.GetRequiredService<CanonCompositionPlan>());
            return builder.BuildConfiguration();
        });

        services.TryAddSingleton<ICanonPipelineCatalog>(sp =>
            sp.GetRequiredService<CanonRuntimeConfiguration>());

        services.TryAddSingleton<ICanonRuntime>(sp =>
        {
            var configuration = sp.GetRequiredService<CanonRuntimeConfiguration>();
            return new CanonRuntime(configuration, sp);
        });

        return services;
    }
}
