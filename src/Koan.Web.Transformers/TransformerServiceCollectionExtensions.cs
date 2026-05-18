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

    internal sealed class TransformerStartupInitializer : IKoanInitializer
    {
        public void Initialize(IServiceCollection services)
        {
            services.TryAddSingleton<ITransformerRegistry, TransformerRegistry>();
            services.AddOptions<TransformerBindings>();
            services.PostConfigure<TransformerBindings>(b => { });

            services.PostConfigure<AutoDiscoveryOptions>(o => { });
            services.AddOptions<AutoDiscoveryOptions>();

            // Defer discovery to runtime; we need the IServiceProvider to construct instances.
            services.PostConfigure<TransformerBindings>(b =>
            {
                b.Bindings.Add(sp =>
                {
                    var cfg = sp.GetService<IConfiguration>();
                    var enabled = cfg.Read(Infrastructure.Constants.Configuration.Transformers.AutoDiscover, true);
                    if (!enabled) return;

                    var reg = sp.GetRequiredService<ITransformerRegistry>();
                    var assemblies = AssemblyCache.Instance.GetAllAssemblies();
                    foreach (var asm in assemblies)
                    {
                        Type[] types;
                        try { types = asm.GetTypes(); } catch { continue; }
                        foreach (var t in types)
                        {
                            if (t.IsAbstract || t.IsInterface) continue;

                            TryAutoRegisterTransformer(sp, reg, t);
                            TryAutoRegisterEnricher(sp, reg, t);
                        }
                    }
                });
            });
        }

        private static void TryAutoRegisterTransformer(IServiceProvider sp, ITransformerRegistry reg, Type t)
        {
            var ifaces = t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTransformer<,>))
                .ToArray();
            if (ifaces.Length == 0) return;

            object? instance = null;
            foreach (var ti in ifaces)
            {
                try
                {
                    instance ??= ActivatorUtilities.CreateInstance(sp, t);
                    var args = ti.GetGenericArguments();
                    var entityType = args[0];
                    var shapeType = args[1];
                    var acceptProp = t.GetProperty("AcceptContentTypes");
                    var acceptValues = (IReadOnlyList<string>?)acceptProp?.GetValue(instance) ?? System.Array.Empty<string>();

                    var regMi = typeof(ITransformerRegistry).GetMethod(nameof(ITransformerRegistry.Register))!;
                    var gm = regMi.MakeGenericMethod(entityType, shapeType);
                    gm.Invoke(reg, new object?[] { instance, acceptValues.ToArray(), (int)TransformerPriority.Discovered });
                }
                catch { /* best effort — keep scanning */ }
            }
        }

        private static void TryAutoRegisterEnricher(IServiceProvider sp, ITransformerRegistry reg, Type t)
        {
            var ifaces = t.GetInterfaces()
                .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityEnricher<>))
                .ToArray();
            if (ifaces.Length == 0) return;

            object? instance = null;
            foreach (var ti in ifaces)
            {
                try
                {
                    instance ??= ActivatorUtilities.CreateInstance(sp, t);
                    var entityType = ti.GetGenericArguments()[0];

                    var regMi = typeof(ITransformerRegistry).GetMethod(nameof(ITransformerRegistry.RegisterEnricher))!;
                    var gm = regMi.MakeGenericMethod(entityType);
                    gm.Invoke(reg, new object?[] { instance, (int)TransformerPriority.Discovered });
                }
                catch { /* best effort — keep scanning */ }
            }
        }

        private sealed class AutoDiscoveryOptions { }
    }
}
