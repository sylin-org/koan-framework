using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Web.Transformers;

public static class TransformerServiceCollectionExtensions
{
    /// <summary>
    /// Register a Terminal-stage transformer (<see cref="IEntityTransformer{TEntity, TShape}"/>)
    /// for one or more content types. Selected via Accept negotiation.
    /// </summary>
    public static IServiceCollection AddEntityTransformer<TEntity, TShape, TTransformer>(this IServiceCollection services, params string[] contentTypes)
        where TTransformer : class, IEntityTransformer<TEntity, TShape>
    {
        services.TryAddSingleton<ITransformerRegistry, TransformerRegistry>();
        services.AddSingleton<IEntityTransformer<TEntity, TShape>, TTransformer>();
        services.AddOptions<TransformerBindings>();
        services.PostConfigure<TransformerBindings>(b =>
        {
            b.Bindings.Add((sp) =>
            {
                var reg = sp.GetRequiredService<ITransformerRegistry>();
                var tr = sp.GetRequiredService<IEntityTransformer<TEntity, TShape>>();
                reg.Register(tr, contentTypes, (int)TransformerPriority.Explicit);
            });
        });
        return services;
    }

    /// <summary>
    /// Register a Pipeline-stage enricher (<see cref="IEntityEnricher{TEntity}"/>). Multiple
    /// enrichers can be registered per entity type; all activated enrichers run in priority order.
    /// Explicit registration takes precedence over auto-discovery for the same type.
    /// </summary>
    public static IServiceCollection AddEntityEnricher<TEntity, TEnricher>(this IServiceCollection services)
        where TEnricher : class, IEntityEnricher<TEntity>
    {
        services.TryAddSingleton<ITransformerRegistry, TransformerRegistry>();
        // Register the concrete TEnricher so the deferred binding below can resolve it directly.
        // Resolving by IEntityEnricher<TEntity> would be ambiguous — multiple enrichers can exist
        // per entity, which is the whole point of the Pipeline stage.
        services.TryAddSingleton<TEnricher>();
        services.AddOptions<TransformerBindings>();
        services.PostConfigure<TransformerBindings>(b =>
        {
            b.Bindings.Add(sp =>
            {
                var reg = sp.GetRequiredService<ITransformerRegistry>();
                var enricher = sp.GetRequiredService<TEnricher>();
                reg.RegisterEnricher<TEntity>(enricher, (int)TransformerPriority.Explicit);
            });
        });
        return services;
    }
}
