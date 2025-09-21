using Aspire.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Koan.Orchestration.Aspire;

/// <summary>
/// Optional interface for KoanAutoRegistrar implementations to provide
/// distributed Aspire resource registration capabilities.
///
/// Modules implementing this interface can automatically register Aspire resources
/// when their assemblies are discovered by the Koan-Aspire integration.
/// </summary>
/// <remarks>
/// This interface is designed to work alongside the existing IKoanAutoRegistrar pattern.
/// Modules should implement both interfaces to provide complete DI and orchestration registration:
///
/// <code>
/// public sealed class KoanAutoRegistrar : IKoanAutoRegistrar, IKoanAspireRegistrar
/// {
///     public void Initialize(IServiceCollection services) { /* DI registration */ }
///     public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env) { /* Aspire resources */ }
/// }
/// </code>
/// </remarks>
public interface IKoanAspireRegistrar
{
    /// <summary>
    /// Register Aspire resources for this module.
    /// Called during AppHost startup for modules that implement this interface.
    /// </summary>
    /// <param name="builder">The Aspire distributed application builder</param>
    /// <param name="configuration">Application configuration for resource setup</param>
    /// <param name="environment">Host environment information for conditional registration</param>
    /// <remarks>
    /// This method should register any container resources, databases, or external services
    /// that the module requires for operation. Use the provided configuration to customize
    /// resource settings based on environment and user preferences.
    ///
    /// <example>
    /// Example implementation for a Postgres module:
    /// <code>
    /// public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
    /// {
    ///     var options = new PostgresOptions();
    ///     new PostgresOptionsConfigurator(cfg).Configure(options);
    ///
    ///     var postgres = builder.AddPostgres("postgres", port: 5432)
    ///         .WithDataVolume()
    ///         .WithEnvironment("POSTGRES_DB", options.Database ?? "Koan")
    ///         .WithEnvironment("POSTGRES_USER", options.Username ?? "postgres");
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    void RegisterAspireResources(
        IDistributedApplicationBuilder builder,
        IConfiguration configuration,
        IHostEnvironment environment);

    /// <summary>
    /// Specify resource registration priority.
    /// Lower numbers register first, allowing infrastructure resources to be available
    /// before application services that depend on them.
    /// </summary>
    /// <value>
    /// Default is 1000. Infrastructure modules (databases, caches) should use 100-500.
    /// Application services should use 1000-2000.
    /// </value>
    /// <remarks>
    /// Priority examples:
    /// - Database providers: 100
    /// - Cache providers: 200
    /// - Message queues: 300
    /// - AI services: 500
    /// - Web applications: 1000
    /// - Background services: 1500
    /// </remarks>
    int Priority => 1000;

    /// <summary>
    /// Specify conditions for resource registration.
    /// Return false to skip registration for this module in the current environment.
    /// </summary>
    /// <param name="configuration">Application configuration for conditional logic</param>
    /// <param name="environment">Host environment information</param>
    /// <returns>True if resources should be registered, false to skip</returns>
    /// <remarks>
    /// Use this method to implement conditional registration logic, such as:
    /// - Only registering heavy resources in development environments
    /// - Skipping registration when external services are configured
    /// - Environment-specific resource selection
    ///
    /// <example>
    /// Example conditional registration:
    /// <code>
    /// public bool ShouldRegister(IConfiguration cfg, IHostEnvironment env)
    /// {
    ///     // Only register Ollama in development environments
    ///     return env.IsDevelopment() && cfg.GetValue("Koan:AI:EnableOllama", false);
    /// }
    /// </code>
    /// </example>
    /// </remarks>
    bool ShouldRegister(IConfiguration configuration, IHostEnvironment environment) => true;
}