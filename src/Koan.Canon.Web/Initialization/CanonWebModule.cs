using System;
using System.Linq;
using System.Reflection;
using Koan.Canon;
using Koan.Canon.Web.Catalog;
using Koan.Canon.Web.Controllers;
using Koan.Canon.Web.Infrastructure;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Web.Extensions;
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

        var plan = services
            .LastOrDefault(static descriptor => descriptor.ServiceType == typeof(CanonCompositionPlan))
            ?.ImplementationInstance as CanonCompositionPlan
            ?? throw new InvalidOperationException(
                "Canon Web requires the host-owned Canon composition plan. Reference Sylin.Koan.Canon.Web and use AddKoan(); " +
                "do not register Canon Web independently.");

        var modelDescriptors = ProjectDescriptors(plan);
        services.AddSingleton<ICanonModelCatalog>(_ => new CanonModelCatalog(modelDescriptors));

        foreach (var descriptor in modelDescriptors)
        {
            RegisterGenericController(services, descriptor.ModelType, typeof(CanonEntitiesController<>), descriptor.Route);
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
            "routes.canon",
            WebConstants.Routes.CanonPrefix + "/{model}",
            source: Koan.Core.Hosting.Bootstrap.BootSettingSource.Custom,
            consumers: new[] { "Koan.Canon.Web.EntitiesController" });
    }

    private static void RegisterGenericController(IServiceCollection services, Type modelType, Type controllerDefinition, string route)
    {
        var method = AddGenericControllerMethod.MakeGenericMethod(modelType);
        _ = method.Invoke(null, new object?[] { services, controllerDefinition, route });
    }

    private static IReadOnlyList<CanonModelDescriptor> ProjectDescriptors(CanonCompositionPlan plan)
    {
        var descriptors = plan.Models.Select(model =>
        {
            var modelType = model.ModelType;
            var slug = ToSlug(modelType.Name);
            return new CanonModelDescriptor(
                modelType,
                slug,
                modelType.Name,
                $"{WebConstants.Routes.CanonPrefix}/{slug}");
        }).ToArray();

        var collision = descriptors
            .GroupBy(static descriptor => descriptor.Slug, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (collision is not null)
        {
            var types = string.Join(", ", collision
                .Select(static descriptor => descriptor.ModelType.FullName ?? descriptor.ModelType.Name)
                .OrderBy(static name => name, StringComparer.Ordinal));
            throw new InvalidOperationException(
                $"Canon Web slug '{collision.Key}' is ambiguous for model types: {types}. Rename one model before AddKoan() composes routes.");
        }

        return descriptors;
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
