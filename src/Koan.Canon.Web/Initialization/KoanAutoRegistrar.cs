using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Canon.Infrastructure;
using Koan.Canon;
using Koan.Canon.Materialization;
using System.Reflection;

namespace Koan.Canon.Web.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Canon.Web";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Ensure MVC sees controllers from this assembly
    var mvc = services.AddControllers();
    mvc.AddApplicationPart(typeof(KoanAutoRegistrar).Assembly);

        // Discover Canon models and register CanonEntityController<TModel> under /api/Canon/{model}
        foreach (var modelType in DiscoverModels())
        {
            var modelName = CanonRegistry.GetModelName(modelType);
            var route = $"{Koan.Canon.Web.Infrastructure.WebConstants.Routes.DefaultPrefix}/{modelName}";
            // Register CanonEntityController<TModel> bound to this model type via GenericControllers helper (by reflection)
            var gcType = Type.GetType("Koan.Web.Extensions.GenericControllers.GenericControllers, Koan.Web.Extensions");
            if (gcType is not null)
            {
                var addGeneric = gcType.GetMethod("AddGenericController", BindingFlags.Public | BindingFlags.Static);
                if (addGeneric is not null)
                {
                    var g = addGeneric.MakeGenericMethod(modelType);
                    _ = g.Invoke(null, new object?[] { services, typeof(Koan.Canon.Web.Controllers.CanonEntityController<>), route });
                }
            }
        }
        // Discover Canon value-objects and register standard EntityController<TVo> under /api/vo/{type}
        foreach (var voType in DiscoverValueObjects())
        {
            var voName = CanonRegistry.GetModelName(voType);
            var route = $"/api/vo/{voName}";
            var gcType = Type.GetType("Koan.Web.Extensions.GenericControllers.GenericControllers, Koan.Web.Extensions");
            if (gcType is not null)
            {
                var addGeneric = gcType.GetMethod("AddGenericController", BindingFlags.Public | BindingFlags.Static);
                if (addGeneric is not null)
                {
                    var g = addGeneric.MakeGenericMethod(voType);
                    _ = g.Invoke(null, new object?[] { services, typeof(Koan.Web.Controllers.EntityController<>), route });
                }
            }
        }
        // Health/metrics are assumed to be added by host; controllers expose endpoints only.

        // Opt-out turnkey: auto-add Canon runtime in web hosts unless disabled via config.
        // Gate: Koan:Canon:AutoRegister (default: true). Idempotent: skips if already added.
        try
        {
            IConfiguration? cfg = null;
            try
            {
                var existing = services.FirstOrDefault(d => d.ServiceType == typeof(IConfiguration));
                cfg = existing?.ImplementationInstance as IConfiguration;
            }
            catch { }

            var enabled = true; // default ON
            if (cfg is not null)
            {
                // Prefer explicit boolean; treat missing as true
                var val = cfg.GetValue<bool?>("Koan:Canon:AutoRegister");
                if (val.HasValue) enabled = val.Value;
            }

            // Skip if Canon already wired (presence of ICanonMaterializer indicates AddKoanCanon ran)
            var already = services.Any(d => d.ServiceType == typeof(ICanonMaterializer));
            if (enabled && !already)
            {
                services.AddKoanCanon();
            }
        }
        catch { }
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
    report.AddModule(ModuleName, ModuleVersion);
    report.AddSetting("routes[0]", "/admin/replay");
    report.AddSetting("routes[1]", "/admin/reproject");
    report.AddSetting("routes[2]", "/models/{model}/views/{view}/{referenceUlid}");
    report.AddSetting("routes[3]", "/models/{model}/views/{view}");
    report.AddSetting("routes[4]", "/policies");
    report.AddSetting("routes[5]", $"{Koan.Canon.Web.Infrastructure.WebConstants.Routes.DefaultPrefix}/{{model}}");
    report.AddSetting("routes[6]", "/api/vo/{type}");
    var autoReg = cfg.GetValue<bool?>("Koan:Canon:AutoRegister") ?? true;
    report.AddSetting("turnkey.autoRegister", autoReg.ToString().ToLowerInvariant());
    }

    private static IEnumerable<Type> DiscoverModels()
    {
        var result = new List<Type>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
            catch { continue; }
            foreach (var t in types)
            {
                if (t is null || !t.IsClass || t.IsAbstract) continue;
                var bt = t.BaseType;
                if (bt is null || !bt.IsGenericType) continue;
                if (bt.GetGenericTypeDefinition() != typeof(Koan.Canon.Model.CanonEntity<>)) continue;
                result.Add(t);
            }
        }
        return result;
    }

    private static IEnumerable<Type> DiscoverValueObjects()
    {
        var result = new List<Type>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var asm in assemblies)
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
            catch { continue; }
            foreach (var t in types)
            {
                if (t is null || !t.IsClass || t.IsAbstract) continue;
                var bt = t.BaseType;
                if (bt is null || !bt.IsGenericType) continue;
                if (bt.GetGenericTypeDefinition() != typeof(Koan.Canon.Model.CanonValueObject<>)) continue;
                result.Add(t);
            }
        }
        return result;
    }
}




