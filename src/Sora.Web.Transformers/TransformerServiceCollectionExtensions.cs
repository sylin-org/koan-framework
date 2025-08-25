using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sora.Core;
using Sora.Core.Extensions;

namespace Sora.Web.Transformers;

public static class TransformerServiceCollectionExtensions
{
    public static IServiceCollection AddEntityTransformer<TEntity, TShape, TTransformer>(this IServiceCollection services, params string[] contentTypes)
        where TTransformer : class, IEntityTransformer<TEntity, TShape>
    {
        services.TryAddSingleton<ITransformerRegistry, TransformerRegistry>();
        services.AddSingleton<IEntityTransformer<TEntity, TShape>, TTransformer>();
        // Store a deferred binding that will be executed by the registry on first use
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

    internal sealed class TransformerStartupInitializer : ISoraInitializer
    {
        public void Initialize(IServiceCollection services)
        {
            services.TryAddSingleton<ITransformerRegistry, TransformerRegistry>();
            services.AddOptions<TransformerBindings>();
            // Auto-discover transformers unless disabled
            services.PostConfigure<TransformerBindings>(b => { });

            services.PostConfigure<AutoDiscoveryOptions>(o => { });
            services.AddOptions<AutoDiscoveryOptions>();

            // Defer discovery to runtime when provider is available (first resolve of registry)
            services.PostConfigure<TransformerBindings>(b =>
            {
                b.Bindings.Add(sp =>
                {
                    var cfg = sp.GetService<IConfiguration>();
                    var enabled = cfg.Read(Infrastructure.Constants.Configuration.Transformers.AutoDiscover, true);
                    if (!enabled) return;

                    var reg = sp.GetRequiredService<ITransformerRegistry>();
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    foreach (var asm in assemblies)
                    {
                        Type[] types;
                        try { types = asm.GetTypes(); } catch { continue; }
                        foreach (var t in types)
                        {
                            if (t.IsAbstract || t.IsInterface) continue;
                            var ifaces = t.GetInterfaces().Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEntityTransformer<,>)).ToArray();
                            if (ifaces.Length == 0) continue;
                            object? tr = null;
                            foreach (var ti in ifaces)
                            {
                                try
                                {
                                    tr ??= ActivatorUtilities.CreateInstance(sp, t);
                                    var args = ti.GetGenericArguments();
                                    var entityType = args[0];
                                    var shapeType = args[1];
                                    var acceptProp = t.GetProperty("AcceptContentTypes");
                                    var acceptValues = (IReadOnlyList<string>?)acceptProp?.GetValue(tr) ?? Array.Empty<string>();

                                    var regMi = typeof(ITransformerRegistry).GetMethod(nameof(ITransformerRegistry.Register))!;
                                    var gm = regMi.MakeGenericMethod(entityType, shapeType);
                                    gm.Invoke(reg, new object?[] { tr, acceptValues.ToArray(), (int)TransformerPriority.Discovered });
                                }
                                catch { /* best effort */ }
                            }
                        }
                    }
                });
            });
        }

        private sealed class AutoDiscoveryOptions { }
    }
}