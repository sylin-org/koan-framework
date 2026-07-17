using System.Reflection;
using Aspire.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Orchestration.Aspire.Extensions;

/// <summary>
/// Result of Koan resource discovery containing builder and discovered resource names
/// </summary>
public class KoanDiscoveryResult
{
    public IDistributedApplicationBuilder Builder { get; init; } = null!;
    public List<string> ResourceNames { get; init; } = new();
}

/// <summary>
/// Extension methods for integrating Koan Framework modules with .NET Aspire
/// through distributed resource registration.
/// </summary>
public static class KoanAspireExtensions
{
    /// <summary>
    /// Automatically discover and register all Koan modules that implement <see cref="IKoanAspireResources"/>.
    /// Discovery follows the capability interface rather than a class-name convention.
    /// </summary>
    /// <param name="builder">The Aspire distributed application builder</param>
    /// <returns>Collection of discovered resource names for application wiring</returns>
    /// <remarks>
    /// This method enables Koan's "Reference = Intent" philosophy for orchestration:
    /// simply referencing a Koan module package automatically registers its required
    /// infrastructure resources in the Aspire application.
    ///
    /// <example>
    /// Basic usage in AppHost Program.cs:
    /// <code>
    /// var builder = DistributedApplication.CreateBuilder(args);
    ///
    /// // Automatically discover and register all Koan module resources
    /// builder.AddKoanDiscoveredResources();
    ///
    /// var app = builder.Build();
    /// await app.RunAsync();
    /// </code>
    /// </example>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when a registrar fails to register resources or when there are
    /// unresolvable resource dependency conflicts.
    /// </exception>
    public static IDistributedApplicationBuilder AddKoanDiscoveredResources(
        this IDistributedApplicationBuilder builder)
    {
        // Configure Aspire dashboard with sensible defaults for Koan applications
        ConfigureKoanAspireDashboard(builder);

        var logger = CreateLogger(builder);

        // Force assembly loading to ensure data providers are available for discovery
        ForceLoadKoanAssemblies(logger);

        var assemblies = KoanAssemblyDiscovery.GetKoanAssemblies();
        var contributors = new List<(IKoanAspireResources Contributor, Type ModuleType, int Priority)>();

        logger?.LogInformation("Koan-Aspire: Starting resource discovery across {AssemblyCount} assemblies", assemblies.Count());

        // Log assembly details for debugging
        logger?.LogInformation("Koan-Aspire: Discovered assemblies: {AssemblyNames}",
            string.Join(", ", assemblies.Select(a => a.GetName().Name)));

        // Discovery phase: find the one module in each assembly that contributes Aspire resources.
        foreach (var assembly in assemblies)
        {
            try
            {
                var moduleTypes = KoanAssemblyDiscovery.GetAspireResourceModuleTypes(assembly);

                if (moduleTypes.Length > 1)
                {
                    throw new InvalidOperationException(
                        $"Assembly '{assembly.GetName().Name}' contains multiple Koan modules that contribute Aspire resources: " +
                        string.Join(", ", moduleTypes.Select(static type => type.FullName)));
                }

                var moduleType = moduleTypes.SingleOrDefault();
                if (moduleType != null)
                {
                    logger?.LogDebug("Koan-Aspire: Found Aspire resource contributor in {AssemblyName}: {ModuleType}",
                        assembly.GetName().Name, moduleType.FullName);

                    var contributor = (IKoanAspireResources)Activator.CreateInstance(moduleType)!;

                    if (contributor.ShouldRegister(builder.Configuration, builder.Environment))
                    {
                        contributors.Add((contributor, moduleType, contributor.Priority));
                        logger?.LogInformation("Koan-Aspire: Queued module {ModuleType} with priority {Priority}",
                            moduleType.FullName, contributor.Priority);
                    }
                    else
                    {
                        logger?.LogDebug("Koan-Aspire: Skipped module {ModuleType} - ShouldRegister returned false",
                            moduleType.FullName);
                    }
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Log warning but continue with other assemblies
                logger?.LogWarning(ex, "Koan-Aspire: Failed to process assembly {AssemblyName}", assembly.GetName().Name);
            }
        }

        logger?.LogInformation("Koan-Aspire: Discovered {ContributorCount} resource contributors", contributors.Count);

        // Registration phase: register in priority order
        var registeredCount = 0;
        foreach (var (contributor, moduleType, priority) in contributors.OrderBy(item => item.Priority))
        {
            try
            {
                logger?.LogDebug("Koan-Aspire: Registering resources for {ModuleType}", moduleType.FullName);

                contributor.RegisterAspireResources(builder, builder.Configuration, builder.Environment);
                registeredCount++;

                logger?.LogDebug("Koan-Aspire: Successfully registered resources for {ModuleType}", moduleType.FullName);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Koan-Aspire: Failed to register Aspire resources for {ModuleType}", moduleType.FullName);
                throw new InvalidOperationException(
                    $"Failed to register Aspire resources for {moduleType.FullName}. " +
                    $"Check the module's RegisterAspireResources implementation and configuration. " +
                    $"Inner exception: {ex.Message}", ex);
            }
        }

        logger?.LogInformation("Koan-Aspire: Successfully registered {RegisteredCount} resource providers", registeredCount);
        return builder;
    }

    /// <summary>
    /// Register a specific Koan module for Aspire integration by type.
    /// Use this method when you want explicit control over which modules are registered
    /// or when you need to register modules that aren't automatically discovered.
    /// </summary>
    /// <typeparam name="TModule">The Koan module that implements <see cref="IKoanAspireResources"/>.</typeparam>
    /// <param name="builder">The Aspire distributed application builder</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// This method is useful for:
    /// - Explicit control over registration order
    /// - Registering modules from assemblies not automatically discovered
    /// - Testing scenarios where you want to register specific modules
    ///
    /// <example>
    /// Explicit module registration:
    /// <code>
    /// builder.AddKoanModule&lt;PostgresDataModule&gt;()
    ///        .AddKoanModule&lt;RedisDataModule&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the registrar type cannot be instantiated or fails during registration.
    /// </exception>
    public static IDistributedApplicationBuilder AddKoanModule<TModule>(
        this IDistributedApplicationBuilder builder)
        where TModule : Koan.Core.KoanModule, IKoanAspireResources, new()
    {
        var logger = CreateLogger(builder);
        var module = new TModule();

        if (module.ShouldRegister(builder.Configuration, builder.Environment))
        {
            try
            {
                logger?.LogDebug("Koan-Aspire: Explicitly registering module {ModuleType}", typeof(TModule).FullName);
                module.RegisterAspireResources(builder, builder.Configuration, builder.Environment);
                logger?.LogDebug("Koan-Aspire: Successfully registered module {ModuleType}", typeof(TModule).FullName);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Koan-Aspire: Failed to register module {ModuleType}", typeof(TModule).FullName);
                throw new InvalidOperationException(
                    $"Failed to register Aspire resources for {typeof(TModule).FullName}. " +
                    $"Check the module's RegisterAspireResources implementation. Inner exception: {ex.Message}", ex);
            }
        }
        else
        {
            logger?.LogDebug("Koan-Aspire: Skipped module {ModuleType} - ShouldRegister returned false", typeof(TModule).FullName);
        }

        return builder;
    }

    /// <summary>
    /// Configure Koan provider selection for Aspire runtime.
    /// This method applies Koan's enhanced provider detection logic to set the
    /// ASPIRE_CONTAINER_RUNTIME environment variable for optimal provider selection.
    /// </summary>
    /// <param name="builder">The Aspire distributed application builder</param>
    /// <param name="preferredProvider">Optional preferred provider (docker, podman, or auto for intelligent selection)</param>
    /// <returns>The builder for method chaining</returns>
    /// <remarks>
    /// Koan provides enhanced provider selection logic that considers:
    /// - Provider availability and health
    /// - Platform-specific optimizations (Windows, Linux, macOS)
    /// - Performance characteristics and resource efficiency
    /// - User preferences and configuration
    ///
    /// <example>
    /// Provider selection examples:
    /// <code>
    /// // Use Koan's intelligent provider selection
    /// builder.UseKoanProviderSelection();
    ///
    /// // Force specific provider
    /// builder.UseKoanProviderSelection("podman");
    /// </code>
    /// </example>
    /// </remarks>
    public static IDistributedApplicationBuilder UseKoanProviderSelection(
        this IDistributedApplicationBuilder builder,
        string? preferredProvider = null)
    {
        var logger = CreateLogger(builder);

        try
        {
            // TODO: Implement Koan's enhanced provider selection logic
            // For now, this is a placeholder that demonstrates the pattern
            var selectedProvider = preferredProvider switch
            {
                "docker" => "docker",
                "podman" => "podman",
                "auto" or null => SelectOptimalProvider(builder.Configuration),
                _ => throw new ArgumentException($"Unknown provider: {preferredProvider}", nameof(preferredProvider))
            };

            if (!string.IsNullOrEmpty(selectedProvider))
            {
                Environment.SetEnvironmentVariable("ASPIRE_CONTAINER_RUNTIME", selectedProvider);
                logger?.LogInformation("Koan-Aspire: Set container runtime to {Provider}", selectedProvider);
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Koan-Aspire: Failed to configure provider selection, using Aspire defaults");
        }

        return builder;
    }

    /// <summary>
    /// Create a logger for Koan-Aspire operations.
    /// </summary>
    private static ILogger? CreateLogger(IDistributedApplicationBuilder builder)
    {
        try
        {
            // Attempt to get logger from builder's service provider if available
            return builder.Services?.BuildServiceProvider()?.GetService<ILoggerFactory>()?.CreateLogger("Koan.Orchestration.Aspire");
        }
        catch
        {
            // If logger creation fails, return null and continue without logging
            return null;
        }
    }

    /// <summary>
    /// Configure Aspire dashboard with sensible defaults for Koan applications.
    /// This ensures the dashboard works out-of-the-box without manual configuration.
    /// </summary>
    private static void ConfigureKoanAspireDashboard(IDistributedApplicationBuilder builder)
    {
        var logger = CreateLogger(builder);

        try
        {
            // For development, disable dashboard to avoid configuration complexity
            // Production environments should configure these properly
            if (builder.Environment.IsDevelopment())
            {
                // Configure dashboard options directly through DI
                builder.Services.PostConfigure<Microsoft.Extensions.Hosting.ConsoleLifetimeOptions>(options =>
                {
                    options.SuppressStatusMessages = true;
                });

                // Set required environment variables for dashboard configuration
                Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://localhost:15888");
                Environment.SetEnvironmentVariable("DOTNET_DASHBOARD_OTLP_ENDPOINT_URL", "http://localhost:4317");
                Environment.SetEnvironmentVariable("DOTNET_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS", "true");

                logger?.LogDebug("Koan-Aspire: Configured development dashboard settings");
            }

            logger?.LogInformation("Koan-Aspire: Dashboard configuration completed");
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Koan-Aspire: Failed to configure dashboard, continuing with defaults");
        }
    }

    /// <summary>
    /// Force load Koan assemblies to ensure they're available for discovery.
    /// This addresses the chicken-and-egg problem where assemblies containing
    /// IKoanAspireResources implementations might not be loaded yet.
    /// </summary>
    private static void ForceLoadKoanAssemblies(ILogger? logger)
    {
        try
        {
            var assembliesInDirectory = Directory.GetFiles(AppDomain.CurrentDomain.BaseDirectory, "Koan.*.dll");

            foreach (var assemblyFile in assembliesInDirectory)
            {
                try
                {
                    var assemblyName = Path.GetFileNameWithoutExtension(assemblyFile);

                    // Skip if already loaded
                    if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == assemblyName))
                        continue;

                    Assembly.LoadFrom(assemblyFile);
                    logger?.LogDebug("Koan-Aspire: Force loaded assembly {AssemblyName}", assemblyName);
                }
                catch (Exception ex)
                {
                    logger?.LogDebug(ex, "Koan-Aspire: Failed to force load assembly {AssemblyFile}", assemblyFile);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Koan-Aspire: Failed to force load assemblies from directory");
        }
    }

    /// <summary>
    /// Select optimal container provider based on Koan's detection logic.
    /// This is a placeholder for the full implementation.
    /// </summary>
    private static string SelectOptimalProvider(IConfiguration configuration)
    {
        // TODO: Implement Koan's existing provider detection logic
        // For now, return docker as default
        return "docker";
    }
}
