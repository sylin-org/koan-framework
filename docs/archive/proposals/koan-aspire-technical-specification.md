---
type: TECHNICAL_SPECIFICATION
domain: orchestration
title: "IKoanAspireRegistrar Interface and Implementation Specification"
audience: [developers, architects]
date: 2025-01-20
status: proposed
---

# Koan-Aspire Integration Technical Specification

**Document Type**: TECHNICAL_SPECIFICATION
**Target Audience**: Framework Developers, Module Authors
**Date**: 2025-01-20
**Status**: Proposed for Implementation

---

## Interface Definitions

### IKoanAspireRegistrar Interface

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Koan.Core;

/// <summary>
/// Optional interface for KoanAutoRegistrar implementations to provide
/// distributed Aspire resource registration capabilities.
/// </summary>
public interface IKoanAspireRegistrar
{
    /// <summary>
    /// Register Aspire resources for this module.
    /// Called during AppHost startup for modules that implement this interface.
    /// </summary>
    /// <param name="builder">The Aspire distributed application builder</param>
    /// <param name="configuration">Application configuration</param>
    /// <param name="environment">Host environment information</param>
    void RegisterAspireResources(
        IDistributedApplicationBuilder builder,
        IConfiguration configuration,
        IHostEnvironment environment);

    /// <summary>
    /// Optional: Specify resource registration priority.
    /// Lower numbers register first. Default is 1000.
    /// </summary>
    int Priority => 1000;

    /// <summary>
    /// Optional: Specify conditions for resource registration.
    /// Return false to skip registration for this module.
    /// </summary>
    /// <param name="configuration">Application configuration</param>
    /// <param name="environment">Host environment information</param>
    bool ShouldRegister(IConfiguration configuration, IHostEnvironment environment) => true;
}
```

### Extended KoanAutoRegistrar Pattern

```csharp
namespace Koan.Data.Connector.Postgres.Initialization;

public sealed class KoanAutoRegistrar : IKoanAutoRegistrar, IKoanAspireRegistrar
{
    public string ModuleName => "Koan.Data.Connector.Postgres";
    public string? ModuleVersion => typeof(KoanAutoRegistrar).Assembly.GetName().Version?.ToString();

    // Existing DI registration - UNCHANGED
    public void Initialize(IServiceCollection services)
    {
        // Existing implementation remains unchanged
        services.AddKoanOptions<PostgresOptions>(Infrastructure.Constants.Configuration.Keys.Section);
        services.AddSingleton<IDataAdapterFactory, PostgresAdapterFactory>();
        // ... rest of existing logic
    }

    // Existing boot reporting - UNCHANGED
    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
    {
        // Existing implementation remains unchanged
        report.AddModule(ModuleName, ModuleVersion);
        // ... rest of existing logic
    }

    // NEW: Aspire resource registration
    public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
    {
        var options = new PostgresOptions();
        new PostgresOptionsConfigurator(cfg).Configure(options);

        var postgres = builder.AddPostgres("postgres", port: 5432)
            .WithDataVolume()
            .WithEnvironment("POSTGRES_DB", options.Database ?? "Koan")
            .WithEnvironment("POSTGRES_USER", options.Username ?? "postgres");

        // Use existing health check patterns if available
        if (!string.IsNullOrEmpty(options.ConnectionString))
        {
            postgres.WithConnectionString(options.ConnectionString);
        }

        // Register health check using Koan's existing health contributor
        postgres.WithHealthCheck("/health/postgres");
    }

    // Optional: Control registration conditions
    public bool ShouldRegister(IConfiguration configuration, IHostEnvironment environment)
    {
        // Only register in Development or when explicitly configured
        return environment.IsDevelopment() ||
               !string.IsNullOrEmpty(configuration["Koan:Data:Postgres:ConnectionString"]);
    }

    // Optional: Register infrastructure before application services
    public int Priority => 100; // Infrastructure components register early
}
```

---

## Discovery and Registration Implementation

### KoanAspireExtensions

```csharp
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Koan.Orchestration.Aspire.Extensions;

public static class KoanAspireExtensions
{
    /// <summary>
    /// Automatically discover and register all Koan modules that implement IKoanAspireRegistrar
    /// </summary>
    public static IDistributedApplicationBuilder AddKoanDiscoveredResources(
        this IDistributedApplicationBuilder builder)
    {
        var assemblies = KoanAssemblyDiscovery.GetKoanAssemblies();
        var registrars = new List<(IKoanAspireRegistrar Registrar, int Priority)>();

        // Discovery phase: find all implementing registrars
        foreach (var assembly in assemblies)
        {
            var registrarType = assembly.GetTypes()
                .FirstOrDefault(t => t.Name == "KoanAutoRegistrar" &&
                               t.GetInterface(nameof(IKoanAspireRegistrar)) != null);

            if (registrarType != null)
            {
                try
                {
                    var registrar = (IKoanAspireRegistrar)Activator.CreateInstance(registrarType)!;

                    // Check if this registrar should register in current environment
                    if (registrar.ShouldRegister(builder.Configuration, builder.Environment))
                    {
                        registrars.Add((registrar, registrar.Priority));
                    }
                }
                catch (Exception ex)
                {
                    // Log warning but continue with other registrars
                    Console.WriteLine($"Warning: Failed to create KoanAspireRegistrar from {registrarType.FullName}: {ex.Message}");
                }
            }
        }

        // Registration phase: register in priority order
        foreach (var (registrar, _) in registrars.OrderBy(r => r.Priority))
        {
            try
            {
                registrar.RegisterAspireResources(builder, builder.Configuration, builder.Environment);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Failed to register Aspire resources for {registrar.GetType().FullName}", ex);
            }
        }

        return builder;
    }

    /// <summary>
    /// Register specific Koan module for Aspire integration
    /// </summary>
    public static IDistributedApplicationBuilder AddKoanModule<TRegistrar>(
        this IDistributedApplicationBuilder builder)
        where TRegistrar : IKoanAspireRegistrar, new()
    {
        var registrar = new TRegistrar();

        if (registrar.ShouldRegister(builder.Configuration, builder.Environment))
        {
            registrar.RegisterAspireResources(builder, builder.Configuration, builder.Environment);
        }

        return builder;
    }
}
```

### Assembly Discovery Helper

```csharp
namespace Koan.Orchestration.Aspire.Discovery;

internal static class KoanAssemblyDiscovery
{
    /// <summary>
    /// Discover all assemblies that contain Koan modules with KoanAutoRegistrar classes
    /// </summary>
    public static IEnumerable<Assembly> GetKoanAssemblies()
    {
        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();

        return loadedAssemblies
            .Where(assembly =>
                assembly.GetName().Name?.StartsWith("Koan.") == true ||
                assembly.GetTypes().Any(t => t.Name == "KoanAutoRegistrar" &&
                                           t.GetInterface("IKoanAutoRegistrar") != null))
            .ToList();
    }
}
```

---

## Module Implementation Examples

### Data Provider Modules

#### Postgres Module
```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
{
    var options = GetConfiguredOptions(cfg);

    var postgres = builder.AddPostgres("postgres", port: options.Port ?? 5432)
        .WithDataVolume()
        .WithEnvironment("POSTGRES_DB", options.Database ?? "Koan")
        .WithEnvironment("POSTGRES_USER", options.Username ?? "postgres")
        .WithEnvironment("POSTGRES_PASSWORD", options.Password ?? "postgres");

    // Add connection string if explicitly configured
    if (!string.IsNullOrEmpty(options.ConnectionString))
    {
        postgres.WithConnectionString(options.ConnectionString);
    }

    // Add health check
    postgres.WithHealthCheck("/");
}

public bool ShouldRegister(IConfiguration cfg, IHostEnvironment env)
{
    // Register if explicitly configured or in development
    return env.IsDevelopment() || HasExplicitConfiguration(cfg);
}

public int Priority => 100; // Infrastructure first
```

#### Redis Module
```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
{
    var options = GetConfiguredOptions(cfg);

    var redis = builder.AddRedis("redis", port: options.Port ?? 6379)
        .WithDataVolume();

    if (!string.IsNullOrEmpty(options.Password))
    {
        redis.WithEnvironment("REDIS_PASSWORD", options.Password);
    }

    if (options.Database.HasValue)
    {
        redis.WithEnvironment("REDIS_DEFAULT_DB", options.Database.Value.ToString());
    }
}

public int Priority => 100; // Infrastructure first
```

#### MongoDB Module
```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
{
    var options = GetConfiguredOptions(cfg);

    var mongo = builder.AddMongoDB("mongodb", port: options.Port ?? 27017)
        .WithDataVolume()
        .WithEnvironment("MONGO_INITDB_ROOT_USERNAME", options.Username ?? "root")
        .WithEnvironment("MONGO_INITDB_ROOT_PASSWORD", options.Password ?? "password");

    if (!string.IsNullOrEmpty(options.Database))
    {
        mongo.WithEnvironment("MONGO_INITDB_DATABASE", options.Database);
    }
}

public int Priority => 100; // Infrastructure first
```

### Application Service Modules

#### AI Provider Module (Ollama)
```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
{
    // Only register in development - heavy AI services opt-in only
    if (env.IsDevelopment())
    {
        var ollama = builder.AddContainer("ollama", "ollama/ollama", "latest")
            .WithVolumeMount("ollama-data", "/root/.ollama")
            .WithEndpoint(11434, 11434, "http")
            .WithEnvironment("OLLAMA_HOST", "0.0.0.0:11434");

        // Add health check
        ollama.WithHealthCheck("/api/tags");
    }
}

public bool ShouldRegister(IConfiguration cfg, IHostEnvironment env)
{
    // Only in development or when explicitly enabled
    return env.IsDevelopment() &&
           cfg.GetValue<bool>("Koan:AI:EnableOllama", false);
}

public int Priority => 500; // Application services after infrastructure
```

#### Web Application Module
```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
{
    var app = builder.AddProject<Projects.MyKoanApp>("app")
        .WithEnvironment("ASPNETCORE_ENVIRONMENT", env.EnvironmentName)
        .WithEnvironment("Koan__Environment", env.EnvironmentName);

    // Reference infrastructure resources registered by other modules
    if (HasResource(builder, "postgres"))
        app.WithReference("postgres");

    if (HasResource(builder, "redis"))
        app.WithReference("redis");

    if (HasResource(builder, "mongodb"))
        app.WithReference("mongodb");

    // Configure Koan-specific environment variables
    ConfigureKoanEnvironment(app, cfg);
}

public int Priority => 1000; // Applications register last

private static void ConfigureKoanEnvironment(IResourceBuilder<ProjectResource> app, IConfiguration cfg)
{
    // Set up Koan configuration from existing patterns
    app.WithEnvironment("Koan__Data__DefaultProvider",
        cfg["Koan:Data:DefaultProvider"] ?? "Postgres");

    // Other Koan-specific configuration...
}

private static bool HasResource(IDistributedApplicationBuilder builder, string resourceName)
{
    return builder.Resources.ContainsKey(resourceName);
}
```

---

## CLI Integration Specification

### Export Command Extension

```bash
# Add new export target alongside existing compose export
Koan export aspire --out ./AppHost/ [--profile <profile>] [--template <template>]

# Traditional workflows remain unchanged
Koan export compose --profile local
Koan up --engine docker
```

### Generated AppHost Structure

```
AppHost/
├── AppHost.csproj          # Aspire AppHost project file
├── Program.cs              # Generated main program
├── Properties/
│   └── launchSettings.json # Launch configuration
└── appsettings.json        # Configuration file
```

### Generated Program.cs Template

```csharp
// Generated by: Koan export aspire
// Date: 2025-01-20
// Profile: Development

using Koan.Orchestration.Aspire.Extensions;

var builder = DistributedApplication.CreateBuilder(args);

// Auto-discover and register all Koan modules with Aspire resources
builder.AddKoanDiscoveredResources();

// Optional: Add non-Koan resources
// var frontend = builder.AddProject<Projects.Frontend>("frontend");

var app = builder.Build();
await app.RunAsync();
```

---

## Configuration Integration

### Koan Configuration Mapping

The Aspire integration should respect existing Koan configuration patterns:

```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
{
    // Use existing Koan configuration configurators
    var options = new PostgresOptions();
    new PostgresOptionsConfigurator(cfg).Configure(options);

    // Map to Aspire resource configuration
    var postgres = builder.AddPostgres("postgres")
        .WithConnectionString(options.ConnectionString ?? GenerateDefaultConnectionString())
        .WithEnvironment("POSTGRES_DB", options.Database ?? "Koan");
}

private string GenerateDefaultConnectionString()
{
    // Use existing Koan connection string generation logic
    return Infrastructure.Constants.Discovery.DefaultLocal;
}
```

### Environment Variable Mapping

```csharp
// Ensure Koan environment variables are properly set in Aspire resources
app.WithEnvironment("Koan__Environment", env.EnvironmentName)
   .WithEnvironment("Koan__Data__DefaultProvider", cfg["Koan:Data:DefaultProvider"] ?? "Postgres")
   .WithEnvironment("Koan__InContainer", "true"); // Signal container environment
```

---

## Error Handling and Validation

### Registration Error Handling

```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
{
    try
    {
        var options = GetConfiguredOptions(cfg);
        ValidateConfiguration(options);

        var resource = CreateAspireResource(builder, options);
        ConfigureResource(resource, options);
    }
    catch (ConfigurationException ex)
    {
        throw new InvalidOperationException(
            $"Invalid configuration for {ModuleName}: {ex.Message}", ex);
    }
    catch (Exception ex)
    {
        throw new InvalidOperationException(
            $"Failed to register Aspire resources for {ModuleName}: {ex.Message}", ex);
    }
}

private void ValidateConfiguration(PostgresOptions options)
{
    if (string.IsNullOrEmpty(options.Database))
        throw new ConfigurationException("Database name is required");

    if (options.Port is <= 0 or > 65535)
        throw new ConfigurationException("Port must be between 1 and 65535");
}
```

### Resource Conflict Detection

```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
{
    const string resourceName = "postgres";

    if (builder.Resources.ContainsKey(resourceName))
    {
        throw new InvalidOperationException(
            $"Resource '{resourceName}' is already registered. " +
            "Check for duplicate module registrations or naming conflicts.");
    }

    builder.AddPostgres(resourceName);
}
```

---

## Testing Strategy

### Unit Testing Module Registrars

```csharp
[Test]
public void RegisterAspireResources_ShouldRegisterPostgres_WhenConfigured()
{
    // Arrange
    var builder = new TestDistributedApplicationBuilder();
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string>
        {
            ["Koan:Data:Postgres:ConnectionString"] = "Host=localhost;Database=test"
        })
        .Build();
    var environment = new TestHostEnvironment { EnvironmentName = "Development" };

    var registrar = new KoanAutoRegistrar();

    // Act
    registrar.RegisterAspireResources(builder, configuration, environment);

    // Assert
    builder.Resources.Should().ContainKey("postgres");
    var postgres = builder.Resources["postgres"];
    postgres.Environment.Should().Contain("POSTGRES_DB", "test");
}
```

### Integration Testing

```csharp
[Test]
public async Task AppHost_ShouldStartSuccessfully_WithKoanModules()
{
    // Arrange
    var appHostBuilder = DistributedApplication.CreateBuilder();
    appHostBuilder.AddKoanDiscoveredResources();

    // Act
    using var app = appHostBuilder.Build();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

    // Assert - should start without exceptions
    await app.StartAsync(cts.Token);
    await app.StopAsync(cts.Token);
}
```

---

## Migration and Compatibility

### Backward Compatibility

- Existing `KoanAutoRegistrar` implementations continue to work unchanged
- `IKoanAspireRegistrar` is completely optional - modules work without it
- Traditional `Koan up` / `Koan export compose` workflows remain available
- No changes to Entity patterns, DI registration, or boot reporting

### Migration Path

1. **Phase 1**: Add `IKoanAspireRegistrar` to core infrastructure modules (Postgres, Redis, MongoDB)
2. **Phase 2**: Add CLI support for `Koan export aspire`
3. **Phase 3**: Add application service modules (Web, AI providers)
4. **Phase 4**: Add advanced features (resource dependencies, health checks)

### Version Compatibility

- Requires .NET Aspire 8.0 or later
- Compatible with existing Koan Framework versions
- No breaking changes to existing Koan APIs

---

## Performance Considerations

### Registration Performance

- Discovery and registration happen at startup time only
- Minimal overhead compared to existing Koan module discovery
- Registration failures are fail-fast with clear error messages

### Resource Efficiency

- Only register resources for modules that are actually referenced
- Respect environment-specific registration conditions
- Use existing Koan configuration without duplication

---

## Security Considerations

### Configuration Security

- Respect existing Koan secret redaction patterns
- Don't expose sensitive configuration in Aspire dashboard
- Use secure defaults for database passwords and connection strings

### Resource Isolation

- Follow Aspire best practices for network isolation
- Maintain separation between development and production resource registration
- Validate configuration to prevent accidental exposure

---

## Documentation Requirements

### Developer Documentation

1. Update Koan.Orchestration documentation to include Aspire export option
2. Provide migration guide for existing applications
3. Add troubleshooting section for common integration issues

### Module Author Documentation

1. Guide for implementing `IKoanAspireRegistrar` in custom modules
2. Best practices for resource naming and configuration mapping
3. Testing patterns for Aspire resource registration

---

This specification provides the technical foundation for implementing distributed Aspire resource registration while maintaining Koan's architectural principles and ensuring backward compatibility.
