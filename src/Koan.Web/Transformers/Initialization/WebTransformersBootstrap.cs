using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Extensions;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Web.Transformers;

namespace Koan.Web.Transformers.Initialization;

/// <summary>
/// Concern-owned transformer registration and provenance invoked by the assembly's single Web module.
/// </summary>
internal static class WebTransformersBootstrap
{
    public static void Register(IServiceCollection services)
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

    public static void Report(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        var enabled = Koan.Core.Configuration.ReadWithSource(
            cfg,
            Infrastructure.Constants.Configuration.Transformers.AutoDiscover,
            true);
        module.AddSetting(
            "AutoDiscover",
            enabled.Value.ToString(),
            source: enabled.Source,
            consumers: new[] { "Koan.Web.Transformers.Registry" });
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
