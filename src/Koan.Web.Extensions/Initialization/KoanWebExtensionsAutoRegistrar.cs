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
public sealed class KoanWebExtensionsAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Web.Extensions";
    public string? ModuleVersion => typeof(KoanWebExtensionsAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Ensure MVC sees controllers from this assembly without requiring apps to call AddApplicationPart explicitly.
        // Safe to call AddControllers multiple times; we only add our ApplicationPart.
        var assembly = typeof(KoanWebExtensionsAutoRegistrar).Assembly;
        var mvc = services.AddControllers(options =>
        {
            // Apply route convention for generic capability controllers
            options.Conventions.Add(new GenericControllers.GenericControllers.RouteConvention());
        });
        mvc.PartManager.ApplicationParts.Add(new AssemblyPart(assembly));
        // Ensure the feature provider is added only once
        mvc.PartManager.FeatureProviders.Add(new GenericControllers.GenericControllers.FeatureProvider());

        // Expose registration extension methods on IServiceCollection (already provided by GenericControllers class)
    }

    public void Describe(Koan.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
    }
}
