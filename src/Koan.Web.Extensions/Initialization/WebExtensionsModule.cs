using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Web.Extensions.GenericControllers;

namespace Koan.Web.Extensions.Initialization;

/// <summary>
/// Auto-registrar that ensures Koan.Web.Extensions controllers are discovered by MVC when the assembly is referenced.
/// </summary>
public sealed class WebExtensionsModule : KoanModule
{
    public override void Register(IServiceCollection services)
    {
        // Ensure MVC sees controllers from this assembly without requiring apps to call AddApplicationPart explicitly.
        // Safe to call AddControllers multiple times; we only add our ApplicationPart.
        var assembly = typeof(WebExtensionsModule).Assembly;
        var mvc = services.AddControllers(options =>
        {
            // Apply route convention for generic capability controllers
            options.Conventions.Add(new GenericControllers.GenericControllers.RouteConvention());
        });
        mvc.PartManager.ApplicationParts.Add(new AssemblyPart(assembly));
        // Ensure the feature provider is added only once
        mvc.PartManager.FeatureProviders.Add(new GenericControllers.GenericControllers.FeatureProvider());

        // ARCH-0092 (§B): discover [RestEntity]-annotated entities and register the terse full-CRUD
        // controller for each (explicit EntityController<T> subclasses win). Runs here so the registrations
        // are in place before MVC's FeatureProvider materializes the controller feature.
        GenericControllers.RestEntityRegistration.RegisterDiscovered(services);

        // Expose registration extension methods on IServiceCollection (already provided by GenericControllers class)
    }

    public override void Report(Koan.Core.Provenance.ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(Version);
    }
}
