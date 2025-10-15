using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Koan.Canon.Domain.Model;
using Koan.Canon.Domain.Runtime;
using Koan.Canon.Web.Catalog;
using Koan.Canon.Web.Controllers;
using Koan.Canon.Web.Infrastructure;
using Koan.Core;
using Koan.Canon.Domain.Pillars;
using Koan.Web.Extensions;
using Koan.Web.Controllers;
using Koan.Web.Extensions.GenericControllers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core.Hosting.Bootstrap;

namespace Koan.Canon.Web.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar
{
    private static readonly MethodInfo AddGenericControllerMethod = typeof(GenericControllers).GetMethod(nameof(GenericControllers.AddGenericController))!;

    public string ModuleName => "Koan.Canon.Web";

    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        FlowPillarManifest.EnsureRegistered();
        services.AddKoanControllersFrom<KoanAutoRegistrar>();
        services.AddCanonRuntime();

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

    public void Describe(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion);
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
        foreach (var modelType in DiscoverTypes(typeof(CanonEntity<>)))
        {
            var slug = ToSlug(modelType.Name);
            var route = $"{WebConstants.Routes.CanonPrefix}/{slug}";
            descriptors.Add(new CanonModelDescriptor(modelType, slug, modelType.Name, route, isValueObject: false));
        }

        if (includeValueObjects)
        {
            foreach (var valueObjectType in DiscoverTypes(typeof(CanonValueObject<>)))
            {
                var slug = ToSlug(valueObjectType.Name);
                var route = $"{WebConstants.Routes.ValueObjectPrefix}/{slug}";
                descriptors.Add(new CanonModelDescriptor(valueObjectType, slug, valueObjectType.Name, route, isValueObject: true));
            }
        }

        return descriptors
            .GroupBy(descriptor => descriptor.ModelType)
            .Select(group => group.First());
    }

    private static IEnumerable<Type> DiscoverTypes(Type openGenericBase)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type?[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types;
            }
            catch
            {
                continue;
            }

            foreach (var type in types)
            {
                if (type is null || type.IsAbstract || !type.IsClass)
                {
                    continue;
                }

                var baseType = type.BaseType;
                if (baseType is null || !baseType.IsGenericType)
                {
                    continue;
                }

                if (baseType.GetGenericTypeDefinition() != openGenericBase)
                {
                    continue;
                }

                yield return type;
            }
        }
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

