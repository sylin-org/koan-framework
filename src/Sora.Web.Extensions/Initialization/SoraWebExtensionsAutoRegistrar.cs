using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sora.Core;
using Sora.Web.Extensions.GenericControllers;

namespace Sora.Web.Extensions.Initialization;

/// <summary>
/// Auto-registrar that ensures Sora.Web.Extensions controllers are discovered by MVC when the assembly is referenced.
/// </summary>
public sealed class SoraWebExtensionsAutoRegistrar : ISoraAutoRegistrar
{
    public string ModuleName => "Sora.Web.Extensions";
    public string? ModuleVersion => typeof(SoraWebExtensionsAutoRegistrar).Assembly.GetName().Version?.ToString();

    public void Initialize(IServiceCollection services)
    {
        // Ensure MVC sees controllers from this assembly without requiring apps to call AddApplicationPart explicitly.
        // Safe to call AddControllers multiple times; we only add our ApplicationPart.
        var assembly = typeof(SoraWebExtensionsAutoRegistrar).Assembly;
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

    public void Describe(Sora.Core.Hosting.Bootstrap.BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        report.AddModule(ModuleName, ModuleVersion);
    }
}
