using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Koan.Core;
using Koan.Core.Hosting.Bootstrap;
using Koan.Core.Provenance;
using Koan.Orchestration.Aspire;
using Aspire.Hosting;

namespace KoanAspireIntegration.Initialization;

/// <summary>
/// Sample application KoanAutoRegistrar demonstrating Aspire integration
/// for application services with infrastructure dependencies.
/// </summary>
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar, IKoanAspireRegistrar
{
    public string ModuleName => "KoanAspireIntegration.Sample";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    /// <summary>
    /// Register application services for dependency injection
    /// </summary>
    public void Initialize(IServiceCollection services)
    {
        // Application-specific service registration would go here
        // For this sample, we're just using the standard Koan patterns
    }

    /// <summary>
    /// Describe the application module in provenance
    /// </summary>
    public void Describe(ProvenanceModuleWriter module, IConfiguration cfg, IHostEnvironment env)
    {
        module.Describe(ModuleVersion, "Koan-Aspire Integration Sample");
        module.AddSetting("ApplicationName", "Koan-Aspire Integration Sample");
        module.AddSetting("Environment", env.EnvironmentName);
        module.AddSetting("AspireIntegrationEnabled", "true");

        // Report on referenced infrastructure modules
        module.AddNote("This sample application demonstrates Koan-Aspire integration");
        module.AddNote("Dependencies: Postgres (data), Redis (cache), Koan.Web (framework)");
    }

    /// <summary>
    /// Register Aspire resources for this application
    /// </summary>
    public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration configuration, IHostEnvironment environment)
    {
        // For this sample, we demonstrate application-level configuration
        // The actual project registration would typically be done in the AppHost project

        // This sample registrar primarily demonstrates that applications can participate
        // in the Aspire registration pipeline alongside infrastructure modules

        // Application-level modules might register:
        // - Custom health checks
        // - Application-specific configuration sources
        // - Cross-cutting concerns like logging or monitoring

        // Note: Infrastructure resources (postgres, redis) are registered by their
        // respective data provider modules (Koan.Data.Connector.Postgres, Koan.Data.Connector.Redis)
    }

    public int Priority => 1000; // Applications register after infrastructure

    public bool ShouldRegister(IConfiguration configuration, IHostEnvironment environment)
    {
        // Always register the application in development
        // In production, this would typically be handled by explicit deployment configuration
        return environment.IsDevelopment();
    }

}
