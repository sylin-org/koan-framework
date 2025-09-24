---
type: SUPPORT
domain: troubleshooting
title: "Koan Framework Troubleshooting Guide"
audience: [developers, support-engineers, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# Koan Framework Troubleshooting Guide

**Document Type**: SUPPORT
**Target Audience**: Developers, Support Engineers
**Last Updated**: 2025-01-17
**Framework Version**: v0.2.18+

---

## Framework Bootstrap Issues

### Auto-Registration Not Working

**Symptom**: Services not found in DI container, missing functionality

**Cause**: Missing `KoanAutoRegistrar` or assembly not loaded

**Solution**:

```csharp
// 1. Verify AddKoan() is called
builder.Services.AddKoan();

// 2. Check for auto-registrar in project
// Create: /Initialization/KoanAutoRegistrar.cs
public class DataAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "MyApp.Data";
    public string? ModuleVersion => "1.0.0";

    public void Initialize(IServiceCollection services)
    {
        // Register services here
    }
}

// 3. Check assembly references
var assemblies = AppDomain.CurrentDomain.GetAssemblies()
    .Where(a => a.GetName().Name?.StartsWith("Koan.") == true);
```

### Boot Report Not Showing Expected Modules

**Symptom**: Missing modules in boot logs

**Diagnostic**:

```csharp
// Enable debug logging
builder.Logging.SetMinimumLevel(LogLevel.Debug);

// Check environment
if (KoanEnv.IsDevelopment)
{
    KoanEnv.DumpSnapshot(logger);
}
```

**Expected Output**:

```
[INFO] Koan:modules data→postgresql
[INFO] Koan:modules web→controllers
[INFO] Koan:modules ai→ollama
```

## Data Layer Issues

### Entity Pattern Problems

**Symptom**: Manual repository injection errors

**Wrong**:

```csharp
public class TodoService
{
    private readonly IRepository<Todo> _repo; // ❌ Don't use repositories
}
```

**Right**:

```csharp
public class TodoService
{
    public async Task<Todo> GetTodo(string id) => await Todo.ById(id); // ✅ Use entity statics
}
```

### Provider Selection Issues

**Symptom**: Wrong provider elected or no provider found

**Diagnostic**:

```csharp
// Check provider capabilities
var capabilities = Data<Todo, string>.QueryCaps;
logger.LogInformation("Query capabilities: {Capabilities}", capabilities.Capabilities);

// Check provider election
logger.LogInformation("Provider elected: {Provider}", capabilities.ProviderName);
```

**Configuration Fix**:

```json
{
  "Koan": {
    "Data": {
      "DefaultProvider": "Postgres",
      "Postgres": {
        "ConnectionString": "Host=localhost;Database=myapp;Username=user;Password=pass"
      }
    }
  }
}
```

### Entity ID Optimization Not Applied

**Symptom**: String IDs still visible in MongoDB/SQL Server

**Cause**: Wrong entity pattern

**Fix**:

```csharp
// ✅ Auto-optimized - uses GUID v7, stored as binary
public class Order : Entity<Order>
{
    public string UserId { get; set; } = "";
}

// ❌ Not optimized - explicit string choice
public class Category : Entity<Category, string>
{
    public string Name { get; set; } = "";
}
```

### Query Performance Issues

**Symptom**: Slow queries or unexpected in-memory processing

**Diagnostic**:

```csharp
// Check if query is pushed down to database
var capabilities = Data<Product, string>.QueryCaps;
if (capabilities.Capabilities.HasFlag(QueryCapabilities.LinqQueries))
{
    // Query will be pushed to database
}
else
{
    // Query will execute in-memory - potential performance issue
}
```

**Fix**:

```csharp
// ✅ Simple queries get pushed down
var products = await Product.Where(p => p.Category == "Electronics");

// ❌ Complex LINQ might not be supported by provider
var complex = await Product.Query()
    .Where(p => p.Tags.Any(t => t.StartsWith("new"))) // May execute in-memory
    .ToArrayAsync();
```

## Web Layer Issues

### Controller Not Found

**Symptom**: 404 errors for API endpoints

**Cause**: Missing controller registration or wrong inheritance

**Fix**:

```csharp
// ✅ Correct controller pattern
[Route("api/[controller]")]
public class TodosController : EntityController<Todo>
{
    // Full CRUD API automatically available
}

// ❌ Wrong - missing route or wrong base class
public class TodosController : ControllerBase // Missing EntityController<T>
{
}
```

### Authentication Issues

**Symptom**: 401 Unauthorized on protected endpoints

**Diagnostic**:

```bash
# Check auth providers
curl http://localhost:5000/.well-known/auth/providers
```

**Configuration Check**:

```json
{
  "Koan": {
    "Web": {
      "Auth": {
        "Providers": {
          "google": {
            "ClientId": "{GOOGLE_CLIENT_ID}",
            "ClientSecret": "{GOOGLE_CLIENT_SECRET}"
          }
        }
      }
    }
  }
}
```

### Health Check Failures

**Symptom**: `/api/health` returns unhealthy

**Diagnostic**:

```bash
# Check individual health endpoints
curl http://localhost:5000/api/health/live    # Liveness
curl http://localhost:5000/api/health/ready   # Readiness
```

**Fix**:

```csharp
// Implement custom health check
public class DatabaseHealthCheck : IHealthContributor
{
    public string Name => "Database";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            await User.Query().Take(1).ToArrayAsync(ct);
            return new HealthReport(Name, true);
        }
        catch (Exception ex)
        {
            return new HealthReport(Name, false, ex.Message);
        }
    }
}
```

## AI Integration Issues

### Ollama Connection Errors

**Symptom**: AI requests fail with connection errors

**Diagnostic**:

```bash
# Check if Ollama is running
curl http://localhost:11434/api/tags
```

**Fix**:

```bash
# Install and start Ollama
curl -fsSL https://ollama.ai/install.sh | sh
ollama pull llama2
ollama serve
```

**Configuration**:

```json
{
  "Koan": {
    "AI": {
      "DefaultProvider": "Ollama",
      "Ollama": {
        "BaseUrl": "http://localhost:11434",
        "DefaultModel": "llama2"
      }
    }
  }
}
```

### Vector Search Not Working

**Symptom**: Empty results from vector search

**Diagnostic**:

```csharp
// Check if embeddings are generated
var doc = await Document.ById("test-id");
logger.LogInformation("Embedding length: {Length}", doc.ContentEmbedding?.Length ?? 0);
```

**Fix**:

```csharp
// Ensure VectorField attribute is present
public class Document : Entity<Document>
{
    public string Content { get; set; } = "";

    [VectorField] // ✅ Required for vector search
    public float[] ContentEmbedding { get; set; } = [];
}

// Generate embeddings
var embedding = await _ai.EmbedAsync(new AiEmbeddingRequest
{
    Input = document.Content
});
document.ContentEmbedding = embedding.Embeddings.FirstOrDefault()?.Vector ?? [];
await document.Save();
```

### Budget Exceeded Errors

**Symptom**: 429 errors from AI endpoints

**Fix**:

```json
{
  "Koan": {
    "AI": {
      "Budget": {
        "MaxTokensPerRequest": 4000,
        "MaxRequestsPerMinute": 100,
        "MaxCostPerDay": 100.0
      }
    }
  }
}
```

## Flow Pipeline Issues

### Flow Key Resolution Errors

**Symptom**: Entities parked with `NO_KEYS` error

**Cause**: Case mismatch between C# properties and JSON serialization

**Diagnostic**:

```csharp
// Check aggregation key extraction
[AggregationKey("inventory.serial")] // ✅ Use camelCase in attribute
public string SerialNumber { get; set; } = ""; // PascalCase property is fine
```

**JSON Structure**:

```json
{
  "id": "dev:123",
  "model": {
    "inventory": {
      "serial": "DEV-001" // Must match aggregation key path
    }
  }
}
```

### External ID Resolution Failures

**Symptom**: Flow entities fail to resolve parent references

**Fix**:

```csharp
// Send with external ID reference
var sensorData = new DynamicFlowEntity<Sensor>
{
    Model = new
    {
        sensor = new { identifier = "TEMP-001" },
        type = "Temperature",
        // Reference parent device by external ID
        reference = new
        {
            device = new
            {
                external = new { oem = "DEV-001" }
            }
        }
    }
};
```

### Flow Adapter Not Starting

**Symptom**: Flow adapters not processing data

**Diagnostic**:

```json
{
  "Koan": {
    "Flow": {
      "Adapters": {
        "AutoStart": true,
        "Include": ["oem:device-sync"], // Check adapter is included
        "Exclude": []
      }
    }
  }
}
```

**Fix**:

```csharp
// Ensure adapter is properly attributed
[FlowAdapter("oem", "device-sync", DefaultSource = "oem-hub")]
public class DeviceAdapter : BackgroundService
{
    // Implementation
}
```

## Messaging Issues

### RabbitMQ Connection Failures

**Symptom**: Messaging not working, connection errors

**Diagnostic**:

```bash
# Check RabbitMQ status
docker ps | grep rabbitmq
curl http://localhost:15672/api/overview
```

**Configuration**:

```json
{
  "Koan": {
    "Messaging": {
      "DefaultBus": "rabbit",
      "Buses": {
        "rabbit": {
          "ConnectionString": "amqp://guest:guest@localhost:5672/",
          "RabbitMq": {
            "ProvisionOnStart": true
          }
        }
      }
    }
  }
}
```

### Messages Not Being Handled

**Symptom**: Messages sent but handlers not triggered

**Fix**:

```csharp
// Ensure handler is properly registered
public class OrderProcessor : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // ✅ Correct handler registration
        await this.On<ProcessOrder>(async (message, sp, ct) =>
        {
            // Handle message
        });
    }
}
```

### Dead Letter Queue Issues

**Symptom**: Messages repeatedly failing

**Diagnostic**:

```csharp
// Check dead letter handling
await this.On<DeadLetterMessage>(async (dlq, sp, ct) =>
{
    logger.LogError("Dead letter: {MessageType} - {Error}",
        dlq.OriginalMessageType, dlq.LastError);
});
```

## Storage Issues

### File Upload Failures

**Symptom**: 413 or 415 errors on file uploads

**Configuration**:

```json
{
  "Koan": {
    "Storage": {
      "MaxFileSize": "10MB",
      "AllowedContentTypes": ["image/jpeg", "image/png", "application/pdf"]
    }
  }
}
```

**Fix**:

```csharp
// Implement content validation
public class ContentValidationStep : IStoragePipelineStep
{
    public async Task<StoragePipelineOutcome> OnReceiveAsync(StoragePipelineContext context)
    {
        if (context.Size > 10 * 1024 * 1024)
            return StoragePipelineOutcome.Stop("File too large");

        var allowedTypes = new[] { "image/jpeg", "image/png", "application/pdf" };
        if (!allowedTypes.Contains(context.ContentType))
            return StoragePipelineOutcome.Stop("Invalid content type");

        return StoragePipelineOutcome.Continue;
    }
}
```

### Storage Profile Not Found

**Symptom**: Storage operations fail with profile errors

**Fix**:

```json
{
  "Koan": {
    "Storage": {
      "DefaultProfile": "local",
      "Profiles": {
        "local": {
          "Provider": "local",
          "Container": "files",
          "BasePath": "./storage"
        }
      }
    }
  }
}
```

## Orchestration Issues

### DevHost Engine Not Found

**Symptom**: `Koan up` fails with engine detection errors

**Diagnostic**:

```bash
# Check available engines
Koan doctor
Koan doctor --json
```

**Fix**:

```bash
# Install Docker Desktop or Podman Desktop
# Then force specific engine
Koan up --engine docker
Koan up --engine podman
```

### Port Conflicts

**Symptom**: Services fail to start due to port conflicts

**Fix**:

```bash
# Use different base port
Koan up --base-port 9000

# Handle conflicts
Koan up --conflicts fail  # Fail on conflicts
Koan up --conflicts warn  # Continue with warnings
```

### Container Readiness Timeout

**Symptom**: `Koan up` times out waiting for services

**Diagnostic**:

```bash
# Check container status
Koan status
Koan logs --tail 200

# Use native Docker/Podman commands
docker compose -f .Koan/compose.yml ps
docker compose -f .Koan/compose.yml logs
```

**Fix**:

```bash
# Increase timeout
Koan up --timeout 600

# Check specific service
Koan logs --service postgres
```

## Performance Issues

### Memory Usage

**Symptom**: High memory consumption

**Diagnostic**:

```csharp
// Check for memory leaks in data access
// ❌ Loading all data into memory
var allUsers = await User.All(); // Dangerous with large datasets

// ✅ Use streaming instead
await foreach (var user in User.AllStream(batchSize: 1000))
{
    await ProcessUser(user);
}
```

### Slow Queries

**Symptom**: Database operations taking too long

**Fix**:

```csharp
// ✅ Use proper filtering and pagination
var products = await Product.Query()
    .Where(p => p.Category == category)
    .OrderBy(p => p.Name)
    .Take(50)
    .ToArrayAsync();

// ❌ Don't load everything then filter
var allProducts = await Product.All();
var filtered = allProducts.Where(p => p.Category == category).Take(50);
```

## Configuration Issues

### Environment Variables Not Loading

**Symptom**: Configuration not being read

**Fix**:

```bash
# Use double underscore for nested configuration
export Koan__Data__DefaultProvider=Postgres
export Koan__Data__Postgres__ConnectionString="Host=localhost;Database=app"

# Or use appsettings.json
{
  "Koan": {
    "Data": {
      "DefaultProvider": "Postgres",
      "Postgres": {
        "ConnectionString": "Host=localhost;Database=app"
      }
    }
  }
}
```

### Secrets Not Loading

**Symptom**: Sensitive configuration not found

**Fix**:

```bash
# Use user secrets in development
dotnet user-secrets set "Koan:Data:Postgres:ConnectionString" "Host=localhost;Database=app;Username=user;Password=secret"

# Use environment variables in production
export Koan__Data__Postgres__ConnectionString="Host=prod-db;Database=app;Username=user;Password=secret"
```

## Diagnostic Commands

### Framework Health Check

```csharp
// Check overall framework status
public class FrameworkDiagnostics
{
    public async Task<object> GetStatus()
    {
        return new
        {
            Environment = KoanEnv.Environment,
            IsContainer = KoanEnv.InContainer,
            DataProvider = Data<object, string>.QueryCaps.ProviderName,
            HealthStatus = await GetHealthStatus(),
            Version = GetFrameworkVersion()
        };
    }
}
```

### Enable Debug Logging

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Koan": "Debug",
      "Koan.Data": "Trace"
    }
  }
}
```

### Common Debug Patterns

```csharp
// Check boot report
KoanEnv.DumpSnapshot(logger);

// Check provider capabilities
var caps = Data<MyEntity, string>.QueryCaps;
logger.LogInformation("Provider: {Provider}, Capabilities: {Capabilities}",
    caps.ProviderName, caps.Capabilities);

// Check entity optimization status
var optimizationInfo = StorageOptimization.GetOptimizationInfo<MyEntity>();
logger.LogInformation("Entity optimization: {Type} - {Reason}",
    optimizationInfo.OptimizationType, optimizationInfo.Reason);
```

## Getting Help

### Framework Logs

Look for structured log entries with these prefixes:

- `[INFO] Koan:modules` - Auto-registration status
- `[INFO] Koan:discover` - Provider discovery
- `[ERROR] Koan:` - Framework errors

### Community Support

- Report issues at: https://github.com/anthropics/claude-code/issues
- Use `/help` command in Leo Botinelly
- Check ADRs in `/documentation/decisions/` for design decisions

---

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+
