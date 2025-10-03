# Koan.Orchestration.Aspire

> ✅ Validated against discovery pipeline, initialization registrar, and self-orchestration services on **2025-09-29**. See [`TECHNICAL.md`](./TECHNICAL.md) for detailed lifecycle diagrams and edge-case coverage.

Distributed Aspire resource registration for Koan Framework modules via the KoanAutoRegistrar pattern.

## Overview

This package enables Koan Framework modules to automatically register their required infrastructure resources (databases, caches, message queues, etc.) with .NET Aspire through a distributed, self-describing approach.

## Key Features

- **Distributed Resource Registration**: Each Koan module self-describes its Aspire resource requirements
- **"Reference = Intent"**: Adding a Koan module package automatically enables both DI and orchestration
- **Enhanced Provider Selection**: Leverages Koan's intelligent Docker/Podman detection with Aspire's native support
- **Zero Configuration**: Works out-of-the-box with sensible defaults
- **Enterprise Patterns**: Service ownership model scales across distributed teams

## Quick Start

### 1. Install the Package

```bash
dotnet add package Koan.Orchestration.Aspire
```

### 2. Create an AppHost Project

```bash
dotnet new aspire-apphost -n MyApp.AppHost
cd MyApp.AppHost
dotnet add reference ../MyApp/MyApp.csproj
dotnet add package Koan.Orchestration.Aspire
```

### 3. Use Koan Discovery in Program.cs

```csharp
using Koan.Orchestration.Aspire.Extensions;

var builder = DistributedApplication.CreateBuilder(args);

// Automatically discover and register all Koan module resources
builder.AddKoanDiscoveredResources();

// Optional: Configure enhanced provider selection
builder.UseKoanProviderSelection("auto"); // or "docker", "podman"

var app = builder.Build();
await app.RunAsync();
```

### 4. Run Your Application

```bash
dotnet run
```

Your Koan modules will automatically register their required infrastructure resources, and you'll see them in the Aspire dashboard.

## How It Works

### Module Self-Registration

Koan modules implement the `IKoanAspireRegistrar` interface alongside their existing `IKoanAutoRegistrar`:

```csharp
public sealed class KoanAutoRegistrar : IKoanAutoRegistrar, IKoanAspireRegistrar
{
    // Existing DI registration
    public void Initialize(IServiceCollection services)
    {
        services.AddSingleton<IDataAdapterFactory, PostgresAdapterFactory>();
    }

    // NEW: Aspire resource registration
    public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
    {
        var options = new PostgresOptions();
        new PostgresOptionsConfigurator(cfg).Configure(options);

        builder.AddPostgres("postgres", port: 5432)
            .WithDataVolume()
            .WithEnvironment("POSTGRES_DB", options.Database ?? "Koan")
            .WithEnvironment("POSTGRES_USER", options.Username ?? "postgres");
    }
}
```

### Automatic Discovery

The `AddKoanDiscoveredResources()` method:

1. Scans loaded assemblies for Koan modules
2. Finds modules implementing `IKoanAspireRegistrar`
3. Registers resources in priority order
4. Handles configuration mapping and error scenarios

## Configuration

### Priority-Based Registration

Control registration order using the `Priority` property:

```csharp
public int Priority => 100; // Infrastructure first (databases, caches)
public int Priority => 1000; // Applications last (web apps, APIs)
```

### Conditional Registration

Skip registration based on environment or configuration:

```csharp
public bool ShouldRegister(IConfiguration cfg, IHostEnvironment env)
{
    // Only register heavy AI services in development
    return env.IsDevelopment() && cfg.GetValue("Koan:AI:EnableOllama", false);
}
```

### Provider Selection

Use Koan's enhanced provider selection:

```csharp
// Intelligent auto-selection based on availability and platform
builder.UseKoanProviderSelection("auto");

// Force specific provider
builder.UseKoanProviderSelection("podman");

// Use environment variable
Environment.SetEnvironmentVariable("ASPIRE_CONTAINER_RUNTIME", "docker");
```

## Supported Modules

### Infrastructure Modules (Priority 100-500)

- **Koan.Data.Connector.Postgres**: PostgreSQL database with automatic schema management
- **Koan.Data.Connector.Redis**: Redis cache with connection multiplexer
- **Koan.Data.Connector.MongoDB**: MongoDB with authentication and database initialization
- **Koan.Data.Connector.SqlServer**: SQL Server with connection string generation

### Application Modules (Priority 1000+)

- **Koan.AI.Provider.Ollama**: Ollama AI service (development-only by default)
- **Koan.Web**: Web application with dependency injection
- **Koan.Messaging**: Message queue integration

## CLI Integration

Generate AppHost projects using the Koan CLI:

```bash
# Export to Aspire AppHost project
Koan export aspire --out ./AppHost

# Traditional Compose export still available
Koan export compose --profile local
```

## Migration from Docker Compose

### Before (Compose-based)

```bash
Koan up --engine docker
```

### After (Aspire-based)

```bash
# Generate AppHost project
Koan export aspire

# Run with Aspire
dotnet run --project AppHost
```

Both approaches can coexist, allowing gradual migration.

## Advanced Usage

### Explicit Module Registration

For fine-grained control:

```csharp
builder.AddKoanModule<PostgresKoanAutoRegistrar>()
       .AddKoanModule<RedisKoanAutoRegistrar>();
```

### Custom Resource Configuration

Override default settings:

```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
{
    var postgres = builder.AddPostgres("postgres")
        .WithDataVolume()
        .WithEnvironment("POSTGRES_MAX_CONNECTIONS", "200")
        .WithAnnotation("custom-config", "value");
}
```

### Multi-Environment Support

Environment-specific resource configuration:

```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, IConfiguration cfg, IHostEnvironment env)
{
    if (env.IsDevelopment())
    {
        // Development: Local containers
        builder.AddPostgres("postgres").WithDataVolume();
    }
    else
    {
        // Production: Azure Database
        builder.AddAzurePostgresFlexibleServer("postgres");
    }
}
```

## Troubleshooting

### Common Issues

**Resources not appearing in Aspire dashboard**:
- Check that modules implement `IKoanAspireRegistrar`
- Verify `ShouldRegister()` returns true for your environment
- Check logs for registration errors

**Provider selection not working**:
- Ensure Docker/Podman is installed and accessible
- Check `ASPIRE_CONTAINER_RUNTIME` environment variable
- Verify provider health using `Koan doctor`

**Configuration not applied**:
- Verify configuration keys match module expectations
- Check configuration precedence and sources
- Use logging to debug configuration values

## Documentation
- [`TECHNICAL.md`](./TECHNICAL.md) – discovery flow, registrar contract, orchestration modes, self-orchestration, and validation notes.

### Diagnostic Information

Get detailed discovery information:

```csharp
using Koan.Orchestration.Aspire.Extensions;

var assemblies = KoanAssemblyDiscovery.GetDetailedAssemblyInfo();
foreach (var assembly in assemblies)
{
    Console.WriteLine($"{assembly.Name}: HasAspireRegistrar={assembly.HasAspireRegistrar}");
}
```

## Contributing

This package is part of the Koan Framework. See the main repository for contribution guidelines and development setup.

## License

MIT License - see the main Koan Framework repository for details.
