---
type: ARCHITECTURE
domain: framework
title: "Koan Framework Architecture Principles"
audience: [architects, senior-developers, ai-agents]
last_updated: 2025-01-17
framework_version: "v0.2.18+"
status: current
validation: 2025-01-17
---

# Koan Framework Architecture Principles

**Document Type**: ARCHITECTURE
**Target Audience**: Architects, Senior Developers
**Last Updated**: 2025-01-17
**Framework Version**: v0.2.18+

---

## Core Philosophy

### Reference = Intent

```csharp
// Adding a package reference enables functionality
builder.Services.AddKoan();
```

Modules auto-register via `IKoanAutoRegistrar`. No manual service configuration.

### Provider Transparency

```csharp
// Same code works across any storage backend
public class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
}

var todos = await Todo.All(); // PostgreSQL, MongoDB, Vector DB, JSON file
```

Storage backend becomes a deployment concern, not development concern.

### Entity-First Development

```csharp
// Static methods are first-class
var todo = await Todo.ById(id);
var todos = await Todo.Where(t => !t.IsCompleted);

// Instance methods work consistently
await todo.Save();
await todo.Delete();
```

Entities handle their own persistence. No repository pattern needed.

## Design Principles

### 1. Simplicity First

**SoC, KISS, YAGNI, DRY applied consistently.**

```csharp
// ✅ Simple controller - full CRUD API
[Route("api/[controller]")]
public class TodosController : EntityController<Todo> { }

// ❌ Avoid manual CRUD implementation
public class TodosController : ControllerBase
{
    private readonly ITodoRepository _repository;
    // ... boilerplate
}
```

Small modules, explicit composition, clear naming.

### 2. Deterministic Configuration

**Explicit config beats discovery. Fail fast on misconfig.**

```csharp
// Configuration hierarchy (higher overrides lower):
// 1. Provider defaults
// 2. appsettings.json
// 3. Environment variables
// 4. Code overrides

var value = Configuration.Read(cfg, "Koan:SomeKey", defaultValue);
```

### 3. Progressive Complexity

**Start simple, add complexity incrementally.**

```csharp
// Level 1: Basic entity
public class User : Entity<User>
{
    public string Email { get; set; } = "";
}

// Level 2: Business logic
public class User : Entity<User>
{
    public string Email { get; set; } = "";

    public static Task<User[]> ActiveUsers() =>
        Query().Where(u => u.IsActive);
}

// Level 3: Multi-provider
[DataAdapter("redis")]
public class UserSession : Entity<UserSession>
{
    public string UserId { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
}
```

### 4. Escape Hatches Everywhere

**Framework enhances but never constrains.**

```csharp
// Direct SQL when needed
var users = await User.ExecuteSql("SELECT * FROM users WHERE custom_logic = true");

// Custom controllers when needed
public class CustomController : ControllerBase
{
    // Full control over HTTP handling
}
```

## Architecture Patterns

### Auto-Registration Pattern

```csharp
// Modules self-register
public class DataAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Koan.Data";
    public string? ModuleVersion => "v0.2.18";

    public void Initialize(IServiceCollection services)
    {
        services.TryAddSingleton<IDataProviderRegistry, DataProviderRegistry>();
        // Auto-discover providers
    }
}
```

**Boot reports show what got registered:**
```
[INFO] Koan:modules data→postgresql
[INFO] Koan:modules web→controllers
[INFO] Koan:modules ai→ollama
```

### Provider Pattern

```csharp
// Same interface, different implementations
public interface IDataProvider
{
    string Name { get; }
    bool CanServe(Type entityType);
    Task<IDataAdapter<T>> GetAdapterAsync<T>() where T : IEntity;
}

// Election based on capability and configuration
var provider = _registry.GetProvider(typeof(Todo));
```

### Capability Detection

```csharp
// Framework handles provider differences transparently
var capabilities = Data<Todo, string>.QueryCaps;

// Automatic fallback when provider lacks features
if (capabilities.Capabilities.HasFlag(QueryCapabilities.LinqQueries))
{
    // Query pushed to database
}
else
{
    // Query executed in-memory
}
```

## Container-Native Design

### Environment Detection

```csharp
// Built-in environment awareness
if (KoanEnv.IsDevelopment)
{
    // Development-only features
}

if (KoanEnv.InContainer)
{
    // Container-specific configuration
}
```

### Health Checks

```csharp
// Automatic health endpoints
// GET /api/health - overall status
// GET /api/health/live - liveness probe
// GET /api/health/ready - readiness probe

public class DatabaseHealthContributor : IHealthContributor
{
    public string Name => "Database";
    public bool IsCritical => true;

    public async Task<HealthReport> CheckAsync(CancellationToken ct = default)
    {
        var isHealthy = await _connection.CanConnectAsync();
        return new HealthReport(Name, isHealthy, isHealthy ? null : "Connection failed");
    }
}
```

### Configuration Resolution

```csharp
// Container-friendly configuration
{
  "Koan": {
    "Data": {
      "DefaultProvider": "Postgres",
      "Postgres": {
        "ConnectionString": "Host=postgres;Database=myapp"
      }
    }
  }
}

// Environment variable override:
// KOAN__DATA__POSTGRES__CONNECTIONSTRING=Host=prod-db;Database=myapp
```

## Cross-Cutting Concerns

### Service Lifetimes

```csharp
// Singleton: Clients, factories, caches
services.TryAddSingleton<IDataProviderRegistry, DataProviderRegistry>();

// Scoped: HTTP request-scoped services
services.TryAddScoped<IDataContext, DataContext>();

// Transient: Stateless helpers
services.TryAddTransient<IValidator<User>, UserValidator>();
```

### Error Handling

```csharp
// Domain exceptions
public class EntityNotFoundException : KoanException
{
    public EntityNotFoundException(string entityType, string id)
        : base($"{entityType} with ID '{id}' was not found") { }
}

// Consistent HTTP error responses
[HttpGet("{id}")]
public async Task<IActionResult> Get(string id)
{
    var entity = await Todo.ById(id);
    return entity == null ? NotFound() : Ok(entity);
}
```

### Security Defaults

```csharp
// Secure headers applied automatically
// X-Frame-Options: DENY
// X-Content-Type-Options: nosniff
// Referrer-Policy: strict-origin-when-cross-origin

[Authorize] // Multi-provider authentication
public class SecureController : EntityController<SecureEntity> { }
```

## Performance Patterns

### Streaming for Large Datasets

```csharp
// ❌ Memory issues with large datasets
var allTodos = await Todo.All(); // Materializes everything

// ✅ Stream large datasets
await foreach (var todo in Todo.AllStream(batchSize: 1000))
{
    await ProcessTodo(todo);
}
```

### Query Optimization

```csharp
// Pushdown-first: Operations pushed to database when possible
var products = await Product.Query()
    .Where(p => p.Category == "Electronics") // Pushed to DB
    .OrderBy(p => p.Price)                   // Pushed to DB
    .Take(50);                               // Pushed to DB

// Automatic in-memory fallback when provider lacks capability
```

### Batch Operations

```csharp
// Efficient batch processing
var batch = new List<Todo>();
foreach (var item in items)
{
    batch.Add(new Todo { Title = item.Title });
}
await Todo.SaveBatch(batch);
```

## Observability

### Boot Reports

```csharp
// Framework self-documents what's configured
KoanEnv.DumpSnapshot(logger);

// Output:
// [INFO] Koan:discover postgresql: server=localhost... ✓
// [INFO] Koan:modules data→postgresql
// [INFO] Koan:modules web→controllers
// [INFO] Koan:capabilities LinqQueries,Paging,Filtering
```

### Structured Logging

```csharp
// Consistent event IDs across framework
_logger.LogInformation(Events.ProviderElected,
    "Provider {Provider} elected for {EntityType}",
    provider.Name, typeof(T).Name);
```

### Telemetry Integration

```csharp
// OpenTelemetry integration (opt-in)
builder.Services.AddKoan(options =>
{
    options.EnableTelemetry = true;
});
```

## Anti-Patterns

### ❌ Manual Repository Pattern

```csharp
// Wrong: Bypassing framework entity patterns
public class TodoService
{
    private readonly IRepository<Todo> _repo;

    public async Task<Todo> GetAsync(string id) => await _repo.GetAsync(id);
}
```

### ❌ Manual Service Registration

```csharp
// Wrong: Manual DI when framework provides auto-registration
services.AddScoped<IUserRepository, UserRepository>();
services.AddDbContext<MyContext>();
```

### ❌ Configuration Magic Values

```csharp
// Wrong: Hard-coded strings
var connectionString = Configuration["ConnectionStrings:Default"];

// Right: Typed options with constants
var options = Configuration.GetSection(KoanDataOptions.SectionName)
    .Get<KoanDataOptions>();
```

## Framework Evolution

### Extensibility Points

```csharp
// Custom providers via standard interface
public class CustomDataProvider : IDataProvider
{
    public string Name => "custom";
    public bool CanServe(Type entityType) => /* logic */;
    // Implementation...
}

// Custom health contributors
public class CustomHealthCheck : IHealthContributor
{
    // Implementation...
}
```

### Version Compatibility

```csharp
// Interfaces versioned for evolution
public interface IDataProvider // v1
public interface IDataProviderV2 : IDataProvider // v2 with additions

// Feature flags for gradual rollout
if (KoanEnv.Features.EnableExperimentalFeature)
{
    // New functionality
}
```

## Strategic Direction

### Container-Native Excellence

Position as premier choice for containerized .NET applications with sophisticated orchestration capabilities.

### Complex Scenario Simplification

Make traditionally complex integrations (AI, OAuth, CQRS, event sourcing) accessible through sane defaults and minimal configuration.

### Enterprise Developer Experience

Target experienced teams willing to invest in framework-specific expertise for long-term productivity gains.

### Operational Sophistication

Emphasize BootReport observability and self-documenting systems as enterprise-grade operational benefits.

---

**References**:
- [ADR Index](../decisions/index.md) - Complete architectural decision history
- [Provider Transparency Showcase](../guides/data-modeling.md) - Multi-provider examples
- [Auto-Registration Patterns](../reference/core/index.md) - Implementation details

**Last Validation**: 2025-01-17 by Framework Specialist
**Framework Version Tested**: v0.2.18+