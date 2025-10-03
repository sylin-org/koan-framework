# Koan-Aspire Integration: Orchestration-Aware Architecture

## Developer's Guide to Koan/Aspire Orchestration

### What is Orchestration?

**Orchestration** is the automated management of application dependencies and infrastructure. Instead of manually starting databases, caches, and services, orchestration systems handle this automatically based on your application's needs.

Traditional approach (manual):
```bash
# Start PostgreSQL manually
docker run -d -p 5432:5432 -e POSTGRES_PASSWORD=postgres postgres

# Configure connection string
export DATABASE_URL="Host=localhost;Port=5432;..."

# Start your app
dotnet run
```

Orchestrated approach (automatic):
```csharp
// Just add the framework reference - dependencies auto-provision
services.AddKoan(); // Postgres starts automatically when needed
```

### Understanding Orchestration Modes

Koan automatically detects and adapts to different environments:

| Mode | When | How Dependencies Work | Connection Pattern |
|------|------|----------------------|-------------------|
| **SelfOrchestrating** | Local development | App starts containers automatically | `localhost:5432` |
| **DockerCompose** | Containerized development | Compose manages all services | `postgres:5432` |
| **AspireAppHost** | Aspire-managed | Aspire provides service discovery | Auto-resolved |
| **Kubernetes** | Production clusters | K8s manages pods | Cluster DNS |
| **Standalone** | External dependencies | You manage infrastructure | Explicit config |

The magic is that **your code stays the same** across all modes - only connection strings change automatically.

## Getting Started: Step-by-Step Implementation

### Level 1: Minimal Koan Application (5 minutes)

Let's start with the simplest possible Koan application that demonstrates orchestration.

#### Step 1: Create a New Project
```bash
mkdir MyFirstKoanApp
cd MyFirstKoanApp
dotnet new web
```

#### Step 2: Add Koan Dependencies
```xml
<!-- MyFirstKoanApp.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <!-- Adding this reference automatically enables Postgres -->
    <PackageReference Include="Koan.Data.Connector.Postgres" Version="1.0.0" />
  </ItemGroup>
</Project>
```

#### Step 3: Define a Simple Entity
```csharp
// Models/User.cs
using Koan.Core;

public class User : Entity<User>
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    // Id is automatically generated as GUID v7
}
```

#### Step 4: Minimal Program.cs
```csharp
// Program.cs
using Koan.Core;

var builder = WebApplication.CreateBuilder(args);

// This single line:
// 1. Detects you have Koan.Data.Connector.Postgres referenced
// 2. Automatically configures PostgreSQL
// 3. Sets up orchestration-aware connection strings
builder.Services.AddKoan();

var app = builder.Build();

// Simple API endpoints using Entity<T> patterns
app.MapGet("/users", async () => await User.All());
app.MapPost("/users", async (User user) => await user.Save());
app.MapGet("/users/{id}", async (string id) => await User.Get(id));

app.Run();
```

#### Step 5: Run Your Application
```bash
dotnet run
```

**What Just Happened?**
1. Koan detected you need PostgreSQL (because of the package reference)
2. It automatically started a PostgreSQL container
3. Your app connected to it using `localhost:5432`
4. You have a working API with database persistence

**Key Insight**: You never wrote connection strings, Docker commands, or configuration. The **"Reference = Intent"** pattern made Koan handle everything.

#### Testing Your First App
```bash
# Create a user
curl -X POST http://localhost:5000/users \
  -H "Content-Type: application/json" \
  -d '{"name":"Alice","email":"alice@example.com"}'

# Get all users
curl http://localhost:5000/users
```

### Level 2: Adding Redis for Caching (10 minutes)

Now let's add Redis caching to demonstrate multi-service orchestration.

#### Step 1: Add Redis Reference
```xml
<!-- MyFirstKoanApp.csproj -->
<ItemGroup>
  <PackageReference Include="Koan.Data.Connector.Postgres" Version="1.0.0" />
  <!-- Adding this automatically provisions Redis -->
  <PackageReference Include="Koan.Data.Connector.Redis" Version="1.0.0" />
</ItemGroup>
```

#### Step 2: Enhanced Entity with Caching
```csharp
// Models/User.cs
using Koan.Core;
using Koan.Data.Connector.Redis;

public class User : Entity<User>
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";

    // Cache frequently accessed users
    public static async Task<User?> GetCached(string id)
    {
        var cacheKey = $"user:{id}";

        // Try cache first
        var cached = await Redis.Get<User>(cacheKey);
        if (cached != null) return cached;

        // Fallback to database
        var user = await User.Get(id);
        if (user != null)
        {
            await Redis.Set(cacheKey, user, TimeSpan.FromMinutes(5));
        }

        return user;
    }
}
```

#### Step 3: Updated API with Caching
```csharp
// Program.cs
app.MapGet("/users/{id}", async (string id) => await User.GetCached(id));
app.MapGet("/users/{id}/fresh", async (string id) => await User.Get(id)); // Bypasses cache
```

#### Step 4: Run and Test
```bash
dotnet run
```

**What Happened Now?**
1. Koan detected you need both PostgreSQL AND Redis
2. It started both containers automatically
3. Your app connected to both services
4. No configuration needed - just add the package reference

**Key Insight**: The **multi-provider pattern** means you can mix storage types freely. Your entity code stays the same whether using Postgres, MongoDB, or Redis.

#### Observing Orchestration
```bash
# See what containers Koan started
docker ps | grep koan

# You'll see something like:
# postgres-a1b2c3d4  postgres:17
# redis-a1b2c3d4     redis:7.4
```

### Level 3: Docker Compose Mode (15 minutes)

Let's package everything for team development using Docker Compose.

#### Step 1: Create Dockerfile
```dockerfile
# Dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY *.csproj .
RUN dotnet restore
COPY . .
RUN dotnet build -c Release -o /app/build

FROM build AS publish
RUN dotnet publish -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MyFirstKoanApp.dll"]
```

#### Step 2: Create Docker Compose Configuration
```yaml
# docker-compose.yml
version: '3.8'

services:
  postgres:
    image: postgres:17.0
    environment:
      POSTGRES_DB: myapp
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5

  redis:
    image: redis:7.4
    ports:
      - "6379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 3

  myapp:
    build: .
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_healthy
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:8080
```

#### Step 3: Run with Docker Compose
```bash
docker compose up --build
```

**What Changed?**
1. Koan detected `COMPOSE_PROJECT_NAME` environment variable
2. Switched from `SelfOrchestrating` to `DockerCompose` mode
3. Connection strings changed from `localhost:5432` to `postgres:5432`
4. **Your application code didn't change at all**

**Key Insight**: **Orchestration-aware** means the same code works in different deployment environments without modification.

### Level 4: Adding Aspire Service Discovery (20 minutes)

Now let's integrate with .NET Aspire for advanced service discovery.

#### Step 1: Create Aspire AppHost Project
```bash
# Create Aspire solution structure
mkdir MyKoanAspireSolution
cd MyKoanAspireSolution

# Create AppHost project
dotnet new aspire-apphost -n MyApp.AppHost

# Move your existing app
mv ../MyFirstKoanApp ./MyApp.Web

# Create solution
dotnet new sln
dotnet sln add MyApp.AppHost/MyApp.AppHost.csproj
dotnet sln add MyApp.Web/MyApp.Web.csproj
```

#### Step 2: Update AppHost Program.cs
```csharp
// MyApp.AppHost/Program.cs
var builder = DistributedApplication.CreateBuilder(args);

// Define services with Aspire
var postgres = builder.AddPostgres("postgres")
    .WithEnvironment("POSTGRES_DB", "myapp");

var redis = builder.AddRedis("redis");

// Add your Koan app with service references
var myapp = builder.AddProject<Projects.MyApp_Web>("myapp")
    .WithReference(postgres)
    .WithReference(redis);

builder.Build().Run();
```

#### Step 3: Update Web Project for Aspire
```csharp
// MyApp.Web/Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Aspire service discovery
builder.AddServiceDefaults();

// Koan will now use Aspire's service discovery automatically
builder.Services.AddKoan();

var app = builder.Build();

app.MapDefaultEndpoints(); // Aspire health checks and metrics

app.MapGet("/users", async () => await User.All());
app.MapPost("/users", async (User user) => await user.Save());
app.MapGet("/users/{id}", async (string id) => await User.GetCached(id));

app.Run();
```

#### Step 4: Run Aspire Solution
```bash
dotnet run --project MyApp.AppHost
```

**What's Different with Aspire?**
1. Aspire provides service discovery and configuration
2. Koan detects `AspireAppHost` mode automatically
3. Connection strings come from Aspire's service registry
4. You get Aspire's dashboard and monitoring for free

**Key Insight**: **Service discovery** means your app finds dependencies dynamically rather than through hardcoded addresses.

### Level 5: Production-Ready Configuration (30 minutes)

Let's prepare for production deployment with proper configuration management.

#### Step 1: Environment-Aware Configuration
```csharp
// Program.cs - Production-ready version
var builder = WebApplication.CreateBuilder(args);

// Configure based on environment
if (builder.Environment.IsDevelopment())
{
    // Development: Use self-orchestration or Aspire
    builder.AddServiceDefaults();
}
else
{
    // Production: Use external services
    Environment.SetEnvironmentVariable("KOAN_ORCHESTRATION_MODE", "Standalone");
}

builder.Services.AddKoan();

var app = builder.Build();

// Add comprehensive health checks
app.MapHealthChecks("/health");
app.MapHealthChecks("/ready");

// Production logging
if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

app.MapGet("/users", async () => await User.All());
app.MapPost("/users", async (User user) => await user.Save());
app.MapGet("/users/{id}", async (string id) => await User.GetCached(id));

app.Run();
```

#### Step 2: Environment-Specific Configuration
```json
// appsettings.Development.json
{
  "Koan": {
    "Data": {
      "Postgres": {
        "Database": "myapp_dev"
      }
    }
  }
}
```

```json
// appsettings.Production.json
{
  "Koan": {
    "Data": {
      "Postgres": {
        "Username": "app_user",
        "Database": "myapp_production"
      }
    }
  },
  "ConnectionStrings": {
    "postgres": "Host=prod-postgres.company.com;Database=myapp_production;Username=app_user;Password=***",
    "redis": "prod-redis.company.com:6379"
  }
}
```

#### Step 3: Kubernetes Deployment
```yaml
# k8s-deployment.yml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: myapp
spec:
  replicas: 3
  selector:
    matchLabels:
      app: myapp
  template:
    metadata:
      labels:
        app: myapp
    spec:
      containers:
      - name: myapp
        image: myapp:latest
        ports:
        - containerPort: 8080
        env:
        - name: ASPNETCORE_ENVIRONMENT
          value: "Production"
        - name: KOAN_ORCHESTRATION_MODE
          value: "Kubernetes"
        - name: KOAN_DATA_POSTGRES_CONNECTIONSTRING
          valueFrom:
            secretKeyRef:
              name: postgres-secret
              key: connection-string
```

**Production Considerations:**
1. **Standalone mode**: No container orchestration, uses external services
2. **Explicit configuration**: Connection strings come from secure configuration
3. **Health checks**: Kubernetes uses these for liveness/readiness probes
4. **Secrets management**: Sensitive data comes from K8s secrets

## Understanding Key Concepts

### 1. Reference = Intent Pattern

**Traditional approach**: Explicit service registration
```csharp
// Traditional way - lots of boilerplate
services.AddDbContext<MyContext>(options =>
    options.UseNpgsql(connectionString));
services.AddScoped<IUserRepository, UserRepository>();
services.AddStackExchangeRedisCache(options =>
    options.Configuration = redisConnectionString);
```

**Koan approach**: Intent through package references
```csharp
// Just add package references in .csproj:
// <PackageReference Include="Koan.Data.Connector.Postgres" />
// <PackageReference Include="Koan.Data.Connector.Redis" />

// Then just:
services.AddKoan(); // Everything auto-configured
```

**Why this matters**: Less configuration code means fewer bugs and faster development.

### 2. Provider Transparency

Your entity code works the same regardless of storage backend:

```csharp
// This code works with ANY provider
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

// Same API across all storage types
await product.Save();        // Works with Postgres, MongoDB, Redis, etc.
var products = await Product.All(); // Same across all providers
```

**Storage switching example**:
```xml
<!-- Switch from Postgres to MongoDB by changing one reference -->
<!-- <PackageReference Include="Koan.Data.Connector.Postgres" /> -->
<PackageReference Include="Koan.Data.Connector.MongoDB" />
```

**Your entity code doesn't change at all.**

### 3. Orchestration Mode Detection

Koan automatically detects the environment and configures accordingly:

```csharp
// Detection logic (automatic)
if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
{
    if (Environment.GetEnvironmentVariable("ASPIRE_SERVICE_NAME") != null)
        return OrchestrationMode.AspireAppHost;

    if (Environment.GetEnvironmentVariable("COMPOSE_PROJECT_NAME") != null)
        return OrchestrationMode.DockerCompose;

    // Kubernetes detection...
}
else if (IsDockerAvailable())
{
    return OrchestrationMode.SelfOrchestrating;
}
```

**You can override** if needed:
```bash
KOAN_ORCHESTRATION_MODE=Standalone dotnet run
```

### 4. Configuration Hierarchy

Koan resolves configuration in priority order:

1. **Aspire service discovery** (highest priority)
2. **Explicit connection strings** in `appsettings.json`
3. **Environment variables** (`KOAN_DATA_POSTGRES_CONNECTIONSTRING`)
4. **Orchestration-aware defaults** based on detected mode
5. **Fallback defaults** (lowest priority)

Example resolution for PostgreSQL:
```csharp
// 1. Check Aspire first
var aspireConn = configuration.GetConnectionString("postgres");
if (!string.IsNullOrEmpty(aspireConn)) return aspireConn;

// 2. Check explicit configuration
var explicitConn = configuration["ConnectionStrings:postgres"];
if (!string.IsNullOrEmpty(explicitConn)) return explicitConn;

// 3. Check environment variables
var envConn = Environment.GetEnvironmentVariable("KOAN_DATA_POSTGRES_CONNECTIONSTRING");
if (!string.IsNullOrEmpty(envConn)) return envConn;

// 4. Use orchestration-aware default
return orchestrationMode switch
{
    OrchestrationMode.SelfOrchestrating => "Host=localhost;Port=5432;...",
    OrchestrationMode.DockerCompose => "Host=postgres;Port=5432;...",
    // etc.
};
```

## Common Patterns and Best Practices

### 1. Entity Design Patterns

#### Basic Entity
```csharp
public class User : Entity<User>
{
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
    // Id automatically generated as GUID v7
}
```

#### Entity with Custom Key Type
```csharp
public class Product : Entity<Product, int>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    // You manage the Id field manually
}
```

#### Entity with Relationships
```csharp
public class Order : Entity<Order>
{
    public string UserId { get; set; } = "";
    public List<OrderItem> Items { get; set; } = new();

    // Relationship navigation
    public async Task<User?> GetUser() => await User.Get(UserId);
}

public class OrderItem : Entity<OrderItem>
{
    public string OrderId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
}
```

### 2. Caching Patterns

#### Simple Caching
```csharp
public class User : Entity<User>
{
    public static async Task<List<User>> GetActiveUsers()
    {
        const string cacheKey = "active_users";

        var cached = await Redis.Get<List<User>>(cacheKey);
        if (cached != null) return cached;

        var users = await User.Query(u => u.IsActive);
        await Redis.Set(cacheKey, users, TimeSpan.FromMinutes(10));

        return users;
    }
}
```

#### Cache-Aside Pattern
```csharp
public class Product : Entity<Product>
{
    public override async Task<Product> Save()
    {
        var result = await base.Save();

        // Invalidate cache after update
        await Redis.Delete($"product:{Id}");
        await Redis.Delete("featured_products");

        return result;
    }
}
```

### 3. Testing Patterns

#### Unit Testing with Test Containers
```csharp
[TestFixture]
public class UserServiceTests
{
    [SetUp]
    public void Setup()
    {
        // Koan automatically uses test-specific containers
        Environment.SetEnvironmentVariable("KOAN_TEST_MODE", "true");
    }

    [Test]
    public async Task CanCreateAndRetrieveUser()
    {
        var user = new User { Name = "Test User", Email = "test@example.com" };
        await user.Save();

        var retrieved = await User.Get(user.Id);
        Assert.That(retrieved?.Name, Is.EqualTo("Test User"));
    }
}
```

#### Integration Testing
```csharp
public class IntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public IntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task UsersEndpoint_ReturnsSuccessAndCorrectContentType()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/users");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
    }
}
```

## Troubleshooting Guide

### Common Issues and Solutions

#### 1. "Docker not available" in Self-Orchestrating Mode
**Problem**: App can't start containers automatically
```
[ERROR] Docker is not available. Unable to start self-orchestration.
```

**Solutions**:
```bash
# Option 1: Start Docker
# Make sure Docker Desktop is running

# Option 2: Override to Standalone mode
KOAN_ORCHESTRATION_MODE=Standalone dotnet run
```

#### 2. Port Conflicts in Development
**Problem**: Multiple developers or test runs conflict
```
[ERROR] Port 5432 already allocated
```

**Solution**: Koan uses session-based naming
```bash
# Each session gets unique containers
docker ps | grep koan
# postgres-a1b2c3d4  <- Session ID in name

# Clean up old containers
docker container prune
```

#### 3. Connection String Issues
**Problem**: App can't connect to database
```
[ERROR] Connection refused at localhost:5432
```

**Debug steps**:
```bash
# Check orchestration mode detection
curl http://localhost:5000/health

# Check container status
docker ps | grep postgres

# Check logs
docker logs postgres-{sessionId}

# Verify connection string resolution
# Add logging to see what Koan resolved
```

#### 4. Aspire Service Discovery Not Working
**Problem**: App can't find services in Aspire mode
```
[ERROR] Aspire mode detected but no connection string found for 'postgres'
```

**Solution**: Verify AppHost references
```csharp
// In AppHost Program.cs
var postgres = builder.AddPostgres("postgres"); // Name must match
var myapp = builder.AddProject<Projects.MyApp>("myapp")
    .WithReference(postgres); // Reference is required
```

### Debugging Tips

#### 1. Enable Verbose Logging
```json
{
  "Logging": {
    "LogLevel": {
      "Koan": "Debug",
      "Koan.Orchestration": "Trace"
    }
  }
}
```

#### 2. Check Environment Detection
```csharp
// Add temporary logging to see what Koan detects
app.MapGet("/debug", () => new {
    OrchestrationMode = KoanEnv.OrchestrationMode,
    IsInContainer = KoanEnv.InContainer,
    SessionId = KoanEnv.SessionId
});
```

#### 3. Validate Configuration
```bash
# Check effective configuration
curl http://localhost:5000/debug/config

# Check health status
curl http://localhost:5000/health
```

## Advanced Scenarios

### 1. Multi-Tenant Applications

```csharp
public class TenantUser : Entity<TenantUser>
{
    public string TenantId { get; set; } = "";
    public string Email { get; set; } = "";

    public static async Task<List<TenantUser>> GetForTenant(string tenantId)
    {
        return await TenantUser.Query(u => u.TenantId == tenantId);
    }
}

// Tenant-aware API
app.MapGet("/tenants/{tenantId}/users", async (string tenantId) =>
    await TenantUser.GetForTenant(tenantId));
```

### 2. Event-Driven Architecture

```csharp
public class Order : Entity<Order>
{
    public string Status { get; set; } = "pending";

    public override async Task<Order> Save()
    {
        var result = await base.Save();

        // Publish domain events
        await EventBus.Publish(new OrderStatusChanged
        {
            OrderId = Id,
            NewStatus = Status,
            Timestamp = DateTime.UtcNow
        });

        return result;
    }
}
```

### 3. Custom Data Providers

```csharp
// Add support for new storage backend
public class CosmosDbAutoRegistrar : IKoanAutoRegistrar
{
    public bool CanRegister(IServiceCollection services, IConfiguration config)
    {
        return config.GetSection("Koan:Data:CosmosDb").Exists();
    }

    public void Register(IServiceCollection services, IConfiguration config)
    {
        services.Configure<CosmosDbOptions>(config.GetSection("Koan:Data:CosmosDb"));
        services.AddSingleton<IDataProvider<CosmosDb>, CosmosDbProvider>();
    }
}
```

This comprehensive guide takes developers from their first Koan application to production-ready, orchestration-aware solutions. Each level builds on the previous one, introducing new concepts gradually while maintaining the core principle that orchestration should be invisible to application code.

## Overview

The Koan Framework provides comprehensive integration with .NET Aspire while maintaining compatibility with multiple orchestration environments. This document details the orchestration-aware architecture that enables seamless deployment across different environments without code changes.

## Architecture Principles

### 1. Orchestration Mode Detection
The framework automatically detects the orchestration environment and adapts behavior accordingly:

```csharp
public enum OrchestrationMode
{
    SelfOrchestrating,  // App manages its own dependencies via Docker
    DockerCompose,      // Docker Compose manages all services
    Kubernetes,         // Kubernetes orchestrates containers
    AspireAppHost,      // .NET Aspire manages service lifecycle
    Standalone          // External dependencies (production/existing infra)
}
```

### 2. Intelligent Connection String Resolution
Instead of hardcoded connection strings, the framework uses orchestration-aware resolution:

```csharp
public interface IOrchestrationAwareConnectionResolver
{
    string ResolveConnectionString(string serviceName, OrchestrationConnectionHints hints);
}
```

### 3. Environment-Aware Service Discovery
Services automatically discover dependencies based on the orchestration context:

- **SelfOrchestrating**: `localhost:5432` (self-managed containers)
- **DockerCompose**: `postgres:5432` (service names)
- **Kubernetes**: `postgres.default.svc.cluster.local:5432` (cluster DNS)
- **AspireAppHost**: Service discovery integration
- **Standalone**: Explicit configuration required

## Core Components

### OrchestrationAwareConnectionResolver

The central component that resolves connection strings based on environment:

```csharp
public string ResolveConnectionString(string serviceName, OrchestrationConnectionHints hints)
{
    // First check for Aspire connection string (highest priority)
    if (_configuration != null)
    {
        var aspireConnectionString = _configuration.GetConnectionString(serviceName);
        if (!string.IsNullOrEmpty(aspireConnectionString))
        {
            return aspireConnectionString;
        }
    }

    // Use orchestration mode to determine connection strategy
    var orchestrationMode = KoanEnv.OrchestrationMode;

    return orchestrationMode switch
    {
        OrchestrationMode.SelfOrchestrating =>
            hints.SelfOrchestrated ?? $"localhost:{hints.DefaultPort}",

        OrchestrationMode.DockerCompose =>
            hints.DockerCompose ?? $"{hints.ServiceName ?? serviceName}:{hints.DefaultPort}",

        OrchestrationMode.Kubernetes =>
            hints.Kubernetes ?? $"{hints.ServiceName ?? serviceName}.default.svc.cluster.local:{hints.DefaultPort}",

        OrchestrationMode.AspireAppHost =>
            hints.AspireManaged ?? throw new InvalidOperationException(
                $"Aspire mode detected but no connection string found for '{serviceName}'"),

        OrchestrationMode.Standalone =>
            hints.External ?? throw new InvalidOperationException(
                $"Standalone mode detected but no external connection string configured for '{serviceName}'"),

        _ => throw new InvalidOperationException($"Unknown orchestration mode: {orchestrationMode}")
    };
}
```

### KoanEnv Orchestration Detection

The framework detects orchestration mode through environment analysis:

```csharp
public static OrchestrationMode OrchestrationMode
{
    get
    {
        // Check for explicit configuration first
        var configuredMode = Environment.GetEnvironmentVariable("KOAN_ORCHESTRATION_MODE");
        if (!string.IsNullOrEmpty(configuredMode) &&
            Enum.TryParse<OrchestrationMode>(configuredMode, true, out var parsed))
        {
            return parsed;
        }

        // Auto-detect based on environment indicators
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            // In container - check for orchestration indicators
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ASPIRE_SERVICE_NAME")))
                return OrchestrationMode.AspireAppHost;

            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("COMPOSE_PROJECT_NAME")))
                return OrchestrationMode.DockerCompose;

            if (File.Exists("/var/run/secrets/kubernetes.io/serviceaccount/token"))
                return OrchestrationMode.Kubernetes;

            return OrchestrationMode.Standalone;
        }

        // Not in container - check for self-orchestration capability
        if (IsDockerAvailable())
            return OrchestrationMode.SelfOrchestrating;

        return OrchestrationMode.Standalone;
    }
}
```

## Self-Orchestration System

### Overview
When running in `SelfOrchestrating` mode, Koan automatically provisions and manages dependency containers using Docker.

### Components

#### 1. KoanDependencyOrchestrator
Discovers and provisions required dependencies:

```csharp
public async Task StartDependenciesAsync(CancellationToken cancellationToken = default)
{
    // Discover dependencies from loaded assemblies
    var dependencies = DiscoverRequiredDependencies();

    // Start dependencies in priority order
    foreach (var dependency in dependencies.OrderBy(d => d.StartupPriority))
    {
        var containerName = await _containerManager.StartContainerAsync(
            dependency, _sessionId, cancellationToken);

        // Wait for health check
        var isHealthy = await _containerManager.WaitForContainerHealthyAsync(
            containerName, dependency, cancellationToken);
    }
}
```

#### 2. DockerContainerManager
Manages Docker container lifecycle:

```csharp
public async Task<string> StartContainerAsync(
    DependencyDescriptor dependency,
    string sessionId,
    CancellationToken cancellationToken)
{
    var containerName = $"{dependency.Name}-{sessionId}";

    // Build Docker command with environment variables and labels
    var command = BuildDockerRunCommand(dependency, containerName, sessionId);

    // Execute Docker command
    var result = await ExecuteDockerCommand(command, cancellationToken);

    return containerName;
}
```

### Dependency Discovery
The system automatically discovers required dependencies by scanning loaded assemblies:

```csharp
private List<DependencyDescriptor> DiscoverRequiredDependencies()
{
    var dependencies = new List<DependencyDescriptor>();
    var koanAssemblies = KoanEnv.KoanAssemblies;

    var assemblyNames = new HashSet<string>(
        koanAssemblies.Select(a => a.GetName().Name ?? ""),
        StringComparer.OrdinalIgnoreCase);

    // Scan for known data providers
    if (assemblyNames.Contains("Koan.Data.Connector.Postgres"))
        dependencies.Add(CreatePostgresDependency());

    if (assemblyNames.Contains("Koan.Data.Connector.Redis"))
        dependencies.Add(CreateRedisDependency());

    if (assemblyNames.Contains("Koan.AI.Ollama"))
        dependencies.Add(CreateOllamaDependency());

    if (assemblyNames.Contains("Koan.AI.Weaviate"))
        dependencies.Add(CreateWeaviateDependency());

    return dependencies;
}
```

### Environment Variable Management
All containers receive consistent Koan lifecycle variables for proper management:

```csharp
private Dictionary<string, string> CreateEnvironmentVariables(
    string dependencyType,
    Dictionary<string, string>? serviceSpecific = null)
{
    var environment = new Dictionary<string, string>(_koanEnvironmentVariables)
    {
        ["KOAN_DEPENDENCY_TYPE"] = dependencyType
    };

    if (serviceSpecific != null)
    {
        foreach (var kvp in serviceSpecific)
        {
            environment[kvp.Key] = kvp.Value;
        }
    }

    return environment;
}
```

Cached environment variables include:
- `KOAN_SESSION_ID`: Unique session identifier for cleanup
- `KOAN_APP_SID`: Application session ID (alias for compatibility)
- `KOAN_APP_ID`: Application identifier
- `KOAN_APP_INSTANCE`: Unique application instance
- `KOAN_MANAGED_BY`: Set to "self-orchestration"
- `KOAN_DEPENDENCY_TYPE`: The type of dependency (postgres, redis, etc.)

## Data Provider Integration

### PostgreSQL Integration
```csharp
// PostgresOptionsConfigurator.cs
public void Configure(PostgresOptions options)
{
    // Get credentials from configuration
    var username = Configuration.ReadFirst(config, "postgres",
        "Koan:Data:Postgres:Username", "Koan:Data:Username");

    // Use orchestration-aware connection resolution
    var resolver = new OrchestrationAwareConnectionResolver(config);
    var hints = new OrchestrationConnectionHints
    {
        SelfOrchestrated = $"Host=localhost;Port=5432;Database={databaseName};Username={username};Password={password}",
        DockerCompose = $"Host=postgres;Port=5432;Database={databaseName};Username={username};Password={password}",
        Kubernetes = $"Host=postgres.default.svc.cluster.local;Port=5432;Database={databaseName};Username={username};Password={password}",
        AspireManaged = null,  // Aspire provides via service discovery
        External = null,       // Must be explicitly configured
        DefaultPort = 5432,
        ServiceName = "postgres"
    };

    options.ConnectionString = resolver.ResolveConnectionString("postgres", hints);
}
```

### Redis Integration
```csharp
// RedisOptionsConfigurator.cs
public void Configure(RedisOptions options)
{
    var resolver = new OrchestrationAwareConnectionResolver(_cfg);
    var hints = new OrchestrationConnectionHints
    {
        SelfOrchestrated = "localhost:6379",
        DockerCompose = "redis:6379",
        Kubernetes = "redis.default.svc.cluster.local:6379",
        AspireManaged = null,
        External = null,
        DefaultPort = 6379,
        ServiceName = "redis"
    };

    options.ConnectionString = resolver.ResolveConnectionString("redis", hints);
}
```

## Aspire Integration

### Service Discovery Integration
When running under Aspire, the framework integrates with service discovery:

```csharp
public string ResolveConnectionString(string serviceName, OrchestrationConnectionHints hints)
{
    // Aspire connection strings take highest priority
    if (_configuration != null)
    {
        var aspireConnectionString = _configuration.GetConnectionString(serviceName);
        if (!string.IsNullOrEmpty(aspireConnectionString))
        {
            return aspireConnectionString; // Uses Aspire service discovery
        }
    }

    // Fallback to hints-based resolution
    // ...
}
```

### AppHost Configuration
Example Aspire AppHost setup:

```csharp
// Program.cs in AppHost project
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithEnvironment("POSTGRES_DB", "KoanAspireDemo");

var redis = builder.AddRedis("redis");

var koanApp = builder.AddProject<Projects.KoanAspireIntegration>("koan-app")
    .WithReference(postgres)
    .WithReference(redis);

builder.Build().Run();
```

## Sample Application

### Project Structure
```
samples/KoanAspireIntegration/
├── Controllers/TodosController.cs      # Entity-first API controller
├── Models/Todo.cs                      # Koan entity with auto GUID v7
├── Program.cs                          # Minimal API with health checks
├── Dockerfile                          # Multi-stage .NET 9 build
├── docker-compose.yml                  # Full service stack
├── start-standalone.bat/.sh            # Self-orchestration scripts
├── start-compose.bat/.sh               # Docker Compose scripts
└── README.md                           # Usage documentation
```

### Minimal API Configuration
```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

// Add Koan services (auto-detects orchestration mode)
builder.Services.AddKoan();

var app = builder.Build();

// Configure health checks
app.MapHealthChecks("/health");

// Add Todo endpoints using Entity<T> patterns
app.MapGet("/api/todos", async () => await Todo.All());
app.MapPost("/api/todos", async (Todo todo) => await todo.Save());

app.Run();
```

### Entity Model
```csharp
// Models/Todo.cs
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
```

## Startup Scripts

### Cross-Platform Browser Launching
All startup scripts include automatic browser launching:

#### Windows (.bat)
```batch
REM Launch browser after a delay (background task)
start "" powershell -WindowStyle Hidden -Command "Start-Sleep 5; Start-Process 'http://localhost:8080'"

REM Start the application
dotnet run --urls http://localhost:8080
```

#### Linux/Mac (.sh)
```bash
# Launch browser after a delay (background task)
(sleep 5 && {
    if command -v open >/dev/null 2>&1; then
        open "http://localhost:8080"
    elif command -v xdg-open >/dev/null 2>&1; then
        xdg-open "http://localhost:8080"
    fi
}) &

# Start the application
dotnet run --urls http://localhost:8080
```

### Available Scripts

#### Standalone Mode (Self-Orchestration)
- `start-standalone.bat` / `start-standalone.sh`
- Automatically provisions Postgres and Redis containers
- App runs on host, dependencies in containers
- Uses `localhost` connections

#### Docker Compose Mode
- `start-compose.bat` / `start-compose.sh`
- All services (app + dependencies) run in containers
- Uses service name connections
- Includes health checks and dependency ordering

## Configuration Patterns

### Environment Variables
The framework responds to several environment variables:

```bash
# Explicit orchestration mode override
KOAN_ORCHESTRATION_MODE=AspireAppHost

# Container detection (set by Docker/Kubernetes)
DOTNET_RUNNING_IN_CONTAINER=true

# Aspire service discovery
ASPIRE_SERVICE_NAME=koan-app

# Docker Compose detection
COMPOSE_PROJECT_NAME=myproject

# Database configuration
KOAN_DATA_POSTGRES_USERNAME=myuser
KOAN_DATA_POSTGRES_PASSWORD=mypass
KOAN_DATA_POSTGRES_DATABASE=mydb
```

### Configuration Priority
1. **Aspire service discovery** (highest priority)
2. **Explicit connection strings** in configuration
3. **Environment variables** (KOAN_DATA_* patterns)
4. **Orchestration-aware defaults** based on detected mode
5. **Fallback defaults** (localhost for self-orchestration)

### appsettings.json
```json
{
  "Koan": {
    "Data": {
      "Postgres": {
        "Username": "postgres",
        "Password": "postgres",
        "Database": "KoanAspireDemo"
      },
      "DefaultPageSize": 50,
      "MaxPageSize": 1000
    }
  }
}
```

## Docker Infrastructure

### Multi-Stage Dockerfile
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files
COPY samples/KoanAspireIntegration/KoanAspireIntegration.csproj samples/KoanAspireIntegration/
COPY src/ src/

# Restore and build
WORKDIR /src/samples/KoanAspireIntegration
RUN dotnet restore "KoanAspireIntegration.csproj"
RUN dotnet build "KoanAspireIntegration.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "KoanAspireIntegration.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "KoanAspireIntegration.dll"]
```

### Docker Compose Stack
```yaml
version: '3.8'

services:
  postgres:
    image: postgres:17.0
    container_name: koan-postgres
    environment:
      POSTGRES_DB: KoanAspireDemo
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
    ports:
      - "5432:5432"
    healthcheck:
      test: ["CMD-SHELL", "pg_isready -U postgres"]
      interval: 10s
      timeout: 5s
      retries: 5

  redis:
    image: redis:7.4
    container_name: koan-redis
    ports:
      - "6379:6379"
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 3

  koan-app:
    build:
      context: ../../
      dockerfile: samples/KoanAspireIntegration/Dockerfile
    depends_on:
      postgres:
        condition: service_healthy
      redis:
        condition: service_healthy
    ports:
      - "8080:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ASPNETCORE_URLS=http://+:8080
```

## Testing and Validation

### Health Checks
The framework includes comprehensive health checks:

```csharp
// Built-in health checks for dependencies
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});
```

### Logging and Diagnostics
Structured logging provides visibility into orchestration decisions:

```csharp
[KoanEnv][INFO] Environment snapshot:
  EnvironmentName: Development
  OrchestrationMode: SelfOrchestrating
  SessionId: 84061bcb
  KoanAssemblies: 11 loaded

[INFO] Self-orchestration starting 2 dependencies: postgres, redis
[INFO] Self-orchestration starting container: postgres-84061bcb
[INFO] Self-orchestration starting container: redis-84061bcb
```

## Best Practices

### 1. Configuration Management
- Never hardcode connection strings
- Use environment-aware configuration patterns
- Leverage Koan's intelligent resolution system

### 2. Container Lifecycle
- Let self-orchestration handle dependency containers
- Use session-based naming for isolation
- Implement proper cleanup on application shutdown

### 3. Environment Detection
- Trust automatic orchestration mode detection
- Override only when necessary using `KOAN_ORCHESTRATION_MODE`
- Test across all supported orchestration modes

### 4. Aspire Integration
- Use service references in AppHost projects
- Let Aspire handle service discovery
- Maintain fallback configuration for non-Aspire scenarios

### 5. Development Workflow
- Use `start-standalone.bat/.sh` for quickest development iteration
- Use `start-compose.bat/.sh` to test full containerized stack
- Use Aspire AppHost for service discovery testing

## Troubleshooting

### Common Issues

#### 1. Connection String Resolution
**Problem**: Application can't connect to dependencies
**Solution**: Check orchestration mode detection and configuration hierarchy

#### 2. Container Port Conflicts
**Problem**: Self-orchestration fails with "port already allocated"
**Solution**: Clean up existing containers or use different ports

#### 3. Docker Unavailable
**Problem**: Self-orchestration mode selected but Docker not running
**Solution**: Start Docker or explicitly set `KOAN_ORCHESTRATION_MODE=Standalone`

#### 4. Aspire Service Discovery
**Problem**: Aspire mode detected but connection strings not available
**Solution**: Verify service references in AppHost project

### Debugging Commands

```bash
# Check orchestration mode detection
docker ps | grep koan

# View container logs
docker logs koan-postgres-{sessionId}

# Check environment variables
docker inspect koan-postgres-{sessionId} | grep -A 20 Env

# Test health checks
curl http://localhost:8080/health
```

## Advanced Scenarios

### Custom Dependency Providers
Extend the system to support additional dependencies:

```csharp
public class CustomDependencyProvider : IDependencyProvider
{
    public bool CanHandle(Assembly assembly)
    {
        return assembly.GetName().Name == "MyCustomProvider";
    }

    public DependencyDescriptor CreateDependency(IConfiguration config)
    {
        return new DependencyDescriptor
        {
            Name = "mycustom",
            Image = "mycustom:latest",
            Port = 9000,
            // ... configuration
        };
    }
}
```

### Multi-Tenant Scenarios
Session-based isolation supports multi-tenant development:

```csharp
// Each session gets isolated containers
var sessionId = KoanEnv.SessionId; // e.g., "84061bcb"
var postgresContainer = $"postgres-{sessionId}";
var redisContainer = $"redis-{sessionId}";
```

### Production Deployment
For production, use `Standalone` mode with external infrastructure:

```bash
KOAN_ORCHESTRATION_MODE=Standalone
KOAN_DATA_POSTGRES_CONNECTIONSTRING="Host=prod-postgres;Database=myapp"
KOAN_DATA_REDIS_CONNECTIONSTRING="prod-redis:6379"
```

This comprehensive orchestration-aware architecture ensures Koan applications work seamlessly across development, testing, and production environments while maintaining the framework's core principles of simplicity and developer experience.
