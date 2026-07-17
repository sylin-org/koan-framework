using System;
using System.Linq;
using System.Reflection;
using Koan.Canon;
using Koan.Canon.Web.Catalog;
using Koan.Canon.Web.Controllers;
using Koan.Canon.Web.Infrastructure;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Hosting.Registry;
using Koan.Web.Extensions;
using Koan.Web.Controllers;
using Koan.Web.Extensions.GenericControllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Koan.Canon.Web.Initialization;

public sealed class CanonWebModule : KoanModule
{
    private static readonly MethodInfo AddGenericControllerMethod = typeof(GenericControllers).GetMethod(nameof(GenericControllers.AddGenericController))!;

    public override void Register(IServiceCollection services)
    {
        services.AddKoanControllersFrom<CanonWebModule>();

        var modelDescriptors = DiscoverDescriptors(includeValueObjects: true).ToList();
        services.AddSingleton<ICanonModelCatalog>(_ => new CanonModelCatalog(modelDescriptors));

        foreach (var descriptor in modelDescriptors.Where(d => !d.IsValueObject))
        {
            RegisterGenericController(services, descriptor.ModelType, typeof(CanonEntitiesController<>), descriptor.Route);
        }

        foreach (var descriptor in modelDescriptors.Where(d => d.IsValueObject))
        {
            RegisterGenericController(services, descriptor.ModelType, typeof(EntityController<>), descriptor.Route);
        }
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
        module.AddSetting(
            "routes.models",
            WebConstants.Routes.Models,
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Custom,
            consumers: new[] { "Koan.Canon.Web.Catalog" });
        module.AddSetting(
            "routes.admin",
            WebConstants.Routes.Admin,
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Custom,
            consumers: new[] { "Koan.Canon.Web.AdminSurface" });
        module.AddSetting(
            "routes.canon",
            WebConstants.Routes.CanonPrefix + "/{model}",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Custom,
            consumers: new[] { "Koan.Canon.Web.EntitiesController" });
        module.AddSetting(
            "routes.valueObjects",
            WebConstants.Routes.ValueObjectPrefix + "/{type}",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Custom,
            consumers: new[] { "Koan.Canon.Web.ValueObjectController" });

        module.AddTool(
            "Canon Admin",
            WebConstants.Routes.Admin,
            "Auto-generated admin surface for Canon models",
            capability: "canon.admin");
    }

    private static void RegisterGenericController(IServiceCollection services, Type modelType, Type controllerDefinition, string route)
    {
        var method = AddGenericControllerMethod.MakeGenericMethod(modelType);
        _ = method.Invoke(null, new object?[] { services, controllerDefinition, route });
    }

    private static IEnumerable<CanonModelDescriptor> DiscoverDescriptors(bool includeValueObjects)
    {
        var descriptors = new List<CanonModelDescriptor>();
        foreach (var modelType in KoanRegistry.GetDiscoveredImplementors(typeof(ICanonModel)))
        {
            var isEntity = IsClosedSubclass(modelType, typeof(CanonEntity<>));
            var isValueObject = IsClosedSubclass(modelType, typeof(CanonValueObject<>));
            if (!isEntity && (!includeValueObjects || !isValueObject))
            {
                continue;
            }

            var slug = ToSlug(modelType.Name);
            var route = isValueObject
                ? $"{WebConstants.Routes.ValueObjectPrefix}/{slug}"
                : $"{WebConstants.Routes.CanonPrefix}/{slug}";
            descriptors.Add(new CanonModelDescriptor(modelType, slug, modelType.Name, route, isValueObject));
        }

        return descriptors
            .GroupBy(descriptor => descriptor.ModelType)
            .Select(group => group.First());
    }

    private static bool IsClosedSubclass(Type type, Type openGenericBase)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == openGenericBase)
            {
                return true;
            }
        }

        return false;
    }

    private static string ToSlug(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        Span<char> buffer = stackalloc char[value.Length * 2];
        var bufferIndex = 0;
        var wasSeparator = true;
        foreach (var ch in value)
        {
            if (char.IsUpper(ch))
            {
                if (!wasSeparator && bufferIndex > 0)
                {
                    buffer[bufferIndex++] = '-';
                }
                buffer[bufferIndex++] = char.ToLowerInvariant(ch);
                wasSeparator = false;
            }
            else if (char.IsLetterOrDigit(ch))
            {
                buffer[bufferIndex++] = char.ToLowerInvariant(ch);
                wasSeparator = false;
            }
            else
            {
                if (!wasSeparator && bufferIndex > 0)
                {
                    buffer[bufferIndex++] = '-';
                }
                wasSeparator = true;
            }
        }

        return new string(buffer[..bufferIndex]);
    }
}
