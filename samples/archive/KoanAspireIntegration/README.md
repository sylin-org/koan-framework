# Koan-Aspire Integration Sample

This sample application demonstrates the revolutionary Koan-Aspire integration through distributed resource registration via the `KoanAutoRegistrar` pattern.

## What This Sample Demonstrates

### 🎯 Core Innovation: "Reference = Intent" for Orchestration

Simply by referencing Koan modules in your project, you automatically get:
- **Dependency Injection** registration (existing functionality)
- **Aspire Resource** registration (NEW functionality)

No manual resource configuration required!

### 🏗️ Distributed Service Ownership

Each Koan module self-describes its orchestration needs:

```csharp
// Postgres module automatically registers PostgreSQL container
// Redis module automatically registers Redis container
// Application module automatically registers web service with dependencies
```

### 🔧 Multi-Provider Excellence

The integration leverages both Koan's enhanced provider detection and Aspire's native multi-provider support:

```bash
# Koan's intelligent provider selection + Aspire's runtime
ASPIRE_CONTAINER_RUNTIME=podman  # or docker
```

## Project Structure

```
KoanAspireIntegration/
├── Controllers/
│   └── TodosController.cs          # Sample API using Entity<T> patterns
├── Models/
│   └── Todo.cs                     # Sample entity demonstrating data access
├── Initialization/
│   └── KoanAutoRegistrar.cs        # Application self-registration for Aspire
├── Program.cs                      # Standard ASP.NET Core + Koan setup
└── appsettings.json               # Koan configuration

KoanAspireIntegration.AppHost/
├── Program.cs                      # Aspire AppHost with Koan discovery
└── KoanAspireIntegration.AppHost.csproj
```

## How It Works

### 1. Automatic Infrastructure Discovery

The AppHost uses Koan's distributed discovery:

```csharp
// This single line discovers and registers ALL Koan module resources
builder.AddKoanDiscoveredResources();

// No need for manual resource definition:
// ❌ builder.AddPostgres("postgres")
// ❌ builder.AddRedis("redis")
// ✅ Modules self-register automatically!
```

### 2. Module Self-Registration

Each referenced Koan module implements `IKoanAspireRegistrar`:

**Postgres Module**:
```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, ...)
{
    builder.AddPostgres("postgres")
        .WithDataVolume()
        .WithEnvironment("POSTGRES_DB", "KoanAspireDemo");
}
```

**Redis Module**:
```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, ...)
{
    builder.AddRedis("redis")
        .WithDataVolume();
}
```

**Application Module** (this sample):
```csharp
public void RegisterAspireResources(IDistributedApplicationBuilder builder, ...)
{
    var app = builder.AddProject<Projects.KoanAspireIntegration>("koan-aspire-sample");
    // Automatically references postgres and redis resources
}
```

### 3. Entity-First Data Access

The sample uses Koan's Entity<T> patterns that work seamlessly across providers:

```csharp
// Create a todo (automatically persisted to Postgres)
var todo = new Todo { Title = "Test Koan-Aspire integration" };
await todo.Save();

// Retrieve todos (automatically queries Postgres)
var todos = await Todo.All();

// Entity patterns work the same regardless of orchestration approach
```

## Running the Sample

### Prerequisites

- .NET 8.0 SDK or later
- Docker Desktop or Podman
- Aspire workload: `dotnet workload install aspire`

### Step 1: Start the Aspire AppHost

```bash
cd samples/KoanAspireIntegration.AppHost
dotnet run
```

This will:
1. **Discover** all Koan modules with Aspire integrations
2. **Register** Postgres and Redis containers automatically
3. **Start** the sample web application
4. **Open** the Aspire dashboard in your browser

### Step 2: Explore the Aspire Dashboard

Visit the Aspire dashboard (typically `http://localhost:15000`) to see:

- **postgres**: PostgreSQL container (registered by Koan.Data.Connector.Postgres)
- **redis**: Redis container (registered by Koan.Data.Connector.Redis)
- **koan-aspire-sample**: Web application (registered by sample app)

All registered automatically without manual configuration!

### Step 3: Test the API

The sample application exposes a Todo API:

```bash
# Get system info
curl http://localhost:5000/api/todos/system-info

# Create a todo
curl -X POST http://localhost:5000/api/todos \
  -H "Content-Type: application/json" \
  -d '{"title": "Test Koan-Aspire integration", "description": "This demonstrates automatic resource registration"}'

# Get all todos
curl http://localhost:5000/api/todos

# Access Swagger UI
open http://localhost:5000/swagger
```

## Configuration

### Provider Selection

The sample demonstrates Koan's enhanced provider selection:

```csharp
// AppHost Program.cs
builder.UseKoanProviderSelection("auto"); // Intelligent selection
// Or force specific provider:
// builder.UseKoanProviderSelection("podman");
```

### Environment-Specific Behavior

The integration respects Koan's environment patterns:

```json
// appsettings.json
{
  "Koan": {
    "Environment": "Development",
    "Data": {
      "DefaultProvider": "Postgres",
      "Postgres": {
        "ConnectionString": "Host=postgres;Port=5432;Database=KoanAspireDemo"
      }
    }
  }
}
```

### Infrastructure Module Configuration

Modules use existing Koan configuration patterns:

- **Postgres**: Configured via `Koan:Data:Postgres:ConnectionString`
- **Redis**: Configured via `Koan:Data:Redis:ConnectionString`
- **Application**: Uses standard ASP.NET Core configuration

## What Makes This Unique

### vs. Vanilla .NET Aspire

**Vanilla Aspire** (centralized):
```csharp
var postgres = builder.AddPostgres("postgres");
var redis = builder.AddRedis("redis");
var app = builder.AddProject<Projects.MyApp>("app")
    .WithReference(postgres)
    .WithReference(redis);
```

**Koan-Enhanced Aspire** (distributed):
```csharp
builder.AddKoanDiscoveredResources(); // Everything automatic!
```

### vs. Traditional Orchestration

**Docker Compose** approach:
- Manual YAML configuration
- Centralized service definition
- No integration with application patterns

**Koan-Aspire** approach:
- Services self-describe their needs
- Framework-integrated configuration
- Enterprise service ownership patterns

## Key Benefits Demonstrated

### 1. Zero Configuration Orchestration
Adding `Koan.Data.Connector.Postgres` package automatically enables PostgreSQL orchestration.

### 2. Enterprise Service Ownership
Teams can own their service's orchestration needs without central coordination.

### 3. Multi-Provider Flexibility
Works with Docker, Podman, and respects developer/environment preferences.

### 4. Framework Integration
Orchestration becomes part of the framework rather than external tooling.

### 5. Developer Experience
- Single command startup: `dotnet run`
- Automatic dependency resolution
- Familiar Koan patterns work unchanged

## Troubleshooting

### Resources Not Appearing in Dashboard

1. **Check module references**: Ensure `Koan.Data.Connector.Postgres` and `Koan.Data.Connector.Redis` are referenced
2. **Verify environment**: Resources only register in Development by default
3. **Check logs**: Look for "Koan-Aspire: Successfully registered" messages

### Connection Issues

1. **Container names**: Resources use consistent names (`postgres`, `redis`)
2. **Port conflicts**: Aspire automatically handles port allocation
3. **Health checks**: Wait for containers to be healthy before connecting

### Provider Selection Issues

1. **Check Docker/Podman**: Ensure your preferred provider is running
2. **Environment variables**: Check `ASPIRE_CONTAINER_RUNTIME` setting
3. **Provider health**: Use `Koan doctor` to check provider status

## Next Steps

This sample validates the core Koan-Aspire integration. Future enhancements:

1. **Additional Modules**: MongoDB, SQL Server, AI providers
2. **CLI Integration**: `Koan export aspire` command
3. **Production Deployment**: Azure Container Apps, Kubernetes export
4. **Advanced Features**: Custom resources, external service integration

The foundation demonstrated here enables enterprise-scale Aspire development with Koan's unique architectural advantages.
