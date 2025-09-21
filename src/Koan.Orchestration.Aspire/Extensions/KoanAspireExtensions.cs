using System.Reflection;
using Aspire.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Orchestration.Aspire.Extensions;

/// <summary>
/// Extension methods for integrating Koan Framework modules with .NET Aspire
/// through distributed resource registration.
/// </summary>
public static class KoanAspireExtensions
{
    /// <summary>
    /// Automatically discover and register all Koan modules that implement IKoanAspireRegistrar.
    /// This method scans loaded assemblies for KoanAutoRegistrar classes that also implement
    /// IKoanAspireRegistrar and calls their RegisterAspireResources method in priority order.
    /// </summary>
    /// <param name="builder">The Aspire distributed application builder</param>
    /// <returns>The builder for method chaining</returns>
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
        var logger = CreateLogger(builder);
        var assemblies = KoanAssemblyDiscovery.GetKoanAssemblies();
        var registrars = new List<(IKoanAspireRegistrar Registrar, Type RegistrarType, int Priority)>();

        logger?.LogInformation("Koan-Aspire: Starting resource discovery across {AssemblyCount} assemblies", assemblies.Count());

        // Discovery phase: find all implementing registrars
        foreach (var assembly in assemblies)
        {
            try
            {
                var registrarType = assembly.GetTypes()
                    .FirstOrDefault(t => t.Name == "KoanAutoRegistrar" &&
                                   t.GetInterface(nameof(IKoanAspireRegistrar)) != null);

                if (registrarType != null)
                {
                    logger?.LogDebug("Koan-Aspire: Found IKoanAspireRegistrar in {AssemblyName}", assembly.GetName().Name);

                    var registrar = (IKoanAspireRegistrar)Activator.CreateInstance(registrarType)!;

                    // Check if this registrar should register in current environment
                    if (registrar.ShouldRegister(builder.Configuration, builder.Environment))
                    {
                        registrars.Add((registrar, registrarType, registrar.Priority));
                        logger?.LogDebug("Koan-Aspire: Queued registrar {RegistrarType} with priority {Priority}",
                            registrarType.FullName, registrar.Priority);
                    }
                    else
                    {
                        logger?.LogDebug("Koan-Aspire: Skipped registrar {RegistrarType} - ShouldRegister returned false",
                            registrarType.FullName);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log warning but continue with other assemblies
                logger?.LogWarning(ex, "Koan-Aspire: Failed to process assembly {AssemblyName}", assembly.GetName().Name);
            }
        }

        logger?.LogInformation("Koan-Aspire: Discovered {RegistrarCount} resource registrars", registrars.Count);

        // Registration phase: register in priority order
        var registeredCount = 0;
        foreach (var (registrar, registrarType, priority) in registrars.OrderBy(r => r.Priority))
        {
            try
            {
                logger?.LogDebug("Koan-Aspire: Registering resources for {RegistrarType}", registrarType.FullName);

                registrar.RegisterAspireResources(builder, builder.Configuration, builder.Environment);
                registeredCount++;

                logger?.LogDebug("Koan-Aspire: Successfully registered resources for {RegistrarType}", registrarType.FullName);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Koan-Aspire: Failed to register Aspire resources for {RegistrarType}", registrarType.FullName);
                throw new InvalidOperationException(
                    $"Failed to register Aspire resources for {registrarType.FullName}. " +
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
    /// <typeparam name="TRegistrar">The KoanAutoRegistrar type that implements IKoanAspireRegistrar</typeparam>
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
    /// builder.AddKoanModule&lt;PostgresKoanAutoRegistrar&gt;()
    ///        .AddKoanModule&lt;RedisKoanAutoRegistrar&gt;();
    /// </code>
    /// </example>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the registrar type cannot be instantiated or fails during registration.
    /// </exception>
    public static IDistributedApplicationBuilder AddKoanModule<TRegistrar>(
        this IDistributedApplicationBuilder builder)
        where TRegistrar : IKoanAspireRegistrar, new()
    {
        var logger = CreateLogger(builder);
        var registrar = new TRegistrar();

        if (registrar.ShouldRegister(builder.Configuration, builder.Environment))
        {
            try
            {
                logger?.LogDebug("Koan-Aspire: Explicitly registering module {ModuleType}", typeof(TRegistrar).FullName);
                registrar.RegisterAspireResources(builder, builder.Configuration, builder.Environment);
                logger?.LogDebug("Koan-Aspire: Successfully registered module {ModuleType}", typeof(TRegistrar).FullName);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Koan-Aspire: Failed to register module {ModuleType}", typeof(TRegistrar).FullName);
                throw new InvalidOperationException(
                    $"Failed to register Aspire resources for {typeof(TRegistrar).FullName}. " +
                    $"Check the module's RegisterAspireResources implementation. Inner exception: {ex.Message}", ex);
            }
        }
        else
        {
            logger?.LogDebug("Koan-Aspire: Skipped module {ModuleType} - ShouldRegister returned false", typeof(TRegistrar).FullName);
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