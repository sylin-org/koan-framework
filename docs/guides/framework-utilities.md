# Framework Utilities Guide

**Purpose**: Centralized catalog of reusable utilities, helpers, and patterns within Koan Framework.
**Audience**: Framework contributors, connector developers, and application developers extending Koan.

**When to Use**: Before writing new helper methods, check this guide to avoid duplicating existing functionality.

---

## Table of Contents

- [Orchestration & Discovery](#orchestration--discovery)
- [Configuration & Options](#configuration--options)
- [Web API Utilities](#web-api-utilities)
- [Data Access Helpers](#data-access-helpers)
- [Common Patterns](#common-patterns)

---

## Orchestration & Discovery

### ConnectionStringParser

**Location**: `src/Koan.Core/Orchestration/ConnectionStringParser.cs`
**Pattern**: Static utility class
**ADR**: [ARCH-0068](../decisions/ARCH-0068-refactoring-strategy-static-vs-di.md) (P2.6)

**Purpose**: Parse and build connection strings for various database providers.

#### Available Methods

```csharp
// PostgreSQL
public static string BuildPostgresConnectionString(string host, int port, string database,
    string? username = null, string? password = null, Dictionary<string, string>? additionalParams = null)

public static (string Host, int Port, string Database, string? Username, string? Password)
    ParsePostgresConnectionString(string connectionString)

// SQL Server
public static string BuildSqlServerConnectionString(string server, string database,
    string? username = null, string? password = null, bool integratedSecurity = false,
    Dictionary<string, string>? additionalParams = null)

public static (string Server, string Database, string? Username, string? Password, bool IntegratedSecurity)
    ParseSqlServerConnectionString(string connectionString)

// MongoDB
public static string BuildMongoConnectionString(string host, int port, string database,
    string? username = null, string? password = null, Dictionary<string, string>? options = null)

public static (string Host, int Port, string Database, string? Username, string? Password)
    ParseMongoConnectionString(string connectionString)

// Redis
public static string BuildRedisConnectionString(string host, int port = 6379,
    string? password = null, int database = 0, Dictionary<string, string>? options = null)

public static (string Host, int Port, string? Password, int Database)
    ParseRedisConnectionString(string connectionString)

// SQLite
public static string BuildSqliteConnectionString(string dataSource,
    Dictionary<string, string>? additionalParams = null)

public static string ParseSqliteConnectionString(string connectionString)
```

#### Usage Example

```csharp
// Building connection strings
var pgConnStr = ConnectionStringParser.BuildPostgresConnectionString(
    host: "localhost",
    port: 5432,
    database: "mydb",
    username: "admin",
    password: "secret"
);
// Result: "Host=localhost;Port=5432;Database=mydb;Username=admin;Password=secret"

// Parsing connection strings
var (host, port, db, user, pwd) = ConnectionStringParser.ParsePostgresConnectionString(
    "Host=localhost;Port=5432;Database=mydb;Username=admin;Password=secret"
);
```

#### When to Use
- Discovery adapters building health check connection strings
- Connector factories parsing configuration
- Test fixtures generating connection strings
- Any scenario requiring provider-specific connection string manipulation

---

### ServiceDiscoveryAdapterBase

**Location**: `src/Koan.Core/Orchestration/ServiceDiscoveryAdapterBase.cs`
**Pattern**: Template Method base class
**ADR**: [ARCH-0068](../decisions/ARCH-0068-refactoring-strategy-static-vs-di.md) (P1.02)

**Purpose**: Base class for service discovery adapters with container/local/Aspire detection logic.

#### Abstract Members to Implement

```csharp
protected abstract string ServiceName { get; }
protected abstract string[] Aliases { get; }
protected abstract Type GetFactoryType();
protected abstract Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken);
```

#### Provided Infrastructure

```csharp
// Container detection (Docker/Podman)
protected bool IsContainerEnvironment { get; }
protected string? DetectContainerService(string serviceName);

// Aspire detection
protected bool IsAspireEnvironment { get; }
protected string? DetectAspireService(string serviceName);

// Local fallback detection
protected string? DetectLocalService(int defaultPort);

// Configuration-based discovery
protected string? GetConfiguredConnectionString();

// Service attribute reading
protected KoanServiceAttribute? GetServiceAttribute();
```

#### Usage Example

```csharp
internal sealed class PostgresDiscoveryAdapter : ServiceDiscoveryAdapterBase
{
    public override string ServiceName => "postgres";
    public override string[] Aliases => new[] { "postgresql", "npgsql" };

    public PostgresDiscoveryAdapter(IConfiguration configuration, ILogger<PostgresDiscoveryAdapter> logger)
        : base(configuration, logger) { }

    protected override Type GetFactoryType() => typeof(PostgresAdapterFactory);

    protected override async Task<bool> ValidateServiceHealth(string serviceUrl, DiscoveryContext context, CancellationToken cancellationToken)
    {
        var connectionString = ConnectionStringParser.BuildPostgresConnectionString(
            serviceUrl,
            context.Parameters.GetValueOrDefault("database", "postgres")
        );

        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return true;
    }
}
```

#### When to Use
- Creating new discovery adapters for data stores
- Implementing autonomous service discovery
- Supporting container, Aspire, and local development environments

---

## Configuration & Options

### OptionsExtensions

**Location**: `src/Koan.Core/Modules/OptionsExtensions.cs`
**Pattern**: Static extension methods
**ADR**: [ARCH-0068](../decisions/ARCH-0068-refactoring-strategy-static-vs-di.md) (New utility)

**Purpose**: Centralize options configuration with validation and layering.

#### Available Methods

```csharp
// Core registration with validation
public static IServiceCollection AddKoanOptions<TOptions>(
    this IServiceCollection services,
    IConfiguration configuration,
    string sectionName,
    Action<TOptions>? configure = null)
    where TOptions : class

// Validation-enabled registration
public static OptionsBuilder<TOptions> AddKoanOptionsWithValidation<TOptions>(
    this IServiceCollection services,
    IConfiguration configuration,
    string sectionName)
    where TOptions : class

// Post-configuration
public static IServiceCollection ConfigureKoanOptions<TOptions>(
    this IServiceCollection services,
    Action<TOptions> configure)
    where TOptions : class
```

#### Usage Example

```csharp
// In connector auto-registrar
public void Register(IServiceCollection services, IConfiguration configuration)
{
    // Basic registration
    services.AddKoanOptions<RedisOptions>(configuration, "Koan:Data:Redis");

    // With validation
    services.AddKoanOptionsWithValidation<PostgresOptions>(configuration, "Koan:Data:Postgres")
        .Validate(opts => !string.IsNullOrEmpty(opts.Host), "PostgreSQL host is required");

    // With post-configuration
    services.AddKoanOptions<MongoOptions>(configuration, "Koan:Data:Mongo", opts =>
    {
        opts.DefaultDatabase ??= "default";
    });
}
```

#### When to Use
- KoanAutoRegistrar implementations
- Options configuration in any Koan component
- Layered configuration scenarios (appsettings → environment → code)

---

## Web API Utilities

### EntityQueryParser

**Location**: `src/Koan.Web/Queries/EntityQueryParser.cs`
**Pattern**: Static helper class
**ADR**: [ARCH-0068](../decisions/ARCH-0068-refactoring-strategy-static-vs-di.md) (P1.10)

**Purpose**: Parse query string parameters for entity queries (filtering, sorting, pagination).

#### Available Methods

```csharp
public static FilterClause? ParseFilter(string? filter)
public static SortClause? ParseSort(string? sort)
public static PaginationParams ParsePagination(int? page, int? pageSize, int defaultPageSize = 20, int maxPageSize = 100)
public static FieldSelection ParseFields(string? fields)
```

#### Usage Example

```csharp
// In EntityController
[HttpGet]
public async Task<IActionResult> GetEntities(
    [FromQuery] string? filter,
    [FromQuery] string? sort,
    [FromQuery] int? page,
    [FromQuery] int? pageSize)
{
    var filterClause = EntityQueryParser.ParseFilter(filter);
    var sortClause = EntityQueryParser.ParseSort(sort);
    var pagination = EntityQueryParser.ParsePagination(page, pageSize);

    var results = await _repository.QueryAsync(filterClause, sortClause, pagination);
    return Ok(results);
}
```

#### When to Use
- Custom EntityController implementations
- API endpoints requiring flexible querying
- GraphQL resolvers
- Any scenario parsing user-supplied query expressions

---

### PatchNormalizer

**Location**: `src/Koan.Web/PatchOps/PatchNormalizer.cs`
**Pattern**: Static helper class
**ADR**: [ARCH-0068](../decisions/ARCH-0068-refactoring-strategy-static-vs-di.md) (P1.10)

**Purpose**: Normalize and validate JSON Patch operations.

#### Available Methods

```csharp
public static PatchOperation[] Normalize(JsonPatchDocument patchDocument)
public static bool ValidatePath(string path, Type entityType)
public static object? CoerceValue(object? value, Type targetType)
public static PatchOperation[] RemoveNoOps(PatchOperation[] operations)
```

#### Usage Example

```csharp
// In EntityController PATCH endpoint
[HttpPatch("{id}")]
public async Task<IActionResult> PatchEntity(Guid id, [FromBody] JsonPatchDocument patchDoc)
{
    var operations = PatchNormalizer.Normalize(patchDoc);

    if (operations.Any(op => !PatchNormalizer.ValidatePath(op.Path, typeof(TEntity))))
    {
        return BadRequest("Invalid patch path");
    }

    var entity = await _repository.GetAsync(id);
    entity.ApplyPatch(operations);
    await entity.SaveAsync();

    return Ok(entity);
}
```

#### When to Use
- PATCH endpoints with JSON Patch support
- Custom entity update logic
- Partial update scenarios
- Validation of user-supplied patch operations

---

### SampleApplicationExtensions

**Location**: `src/Koan.Web/Hosting/SampleApplicationExtensions.cs`
**Pattern**: Static extension methods
**Purpose**: Common setup patterns for sample applications.

#### Available Methods

```csharp
public static WebApplicationBuilder ConfigureSampleApp(this WebApplicationBuilder builder)
public static WebApplication ConfigureSamplePipeline(this WebApplication app)
public static WebApplicationBuilder AddDevCors(this WebApplicationBuilder builder)
```

#### Usage Example

```csharp
// In sample Program.cs
var builder = WebApplication.CreateBuilder(args);
builder.ConfigureSampleApp();  // Adds common services
builder.AddDevCors();           // CORS for local development

var app = builder.Build();
app.ConfigureSamplePipeline();  // Standard middleware setup
app.Run();
```

#### When to Use
- Creating new sample applications
- Standardizing sample project setup
- Quick prototyping with Koan defaults

---

## Data Access Helpers

### Entity Static Methods Pattern

**Location**: Throughout `Entity<T>` and `Entity<T, TKey>` classes
**Pattern**: Static factory methods on entity classes
**Guidance**: [Entity-First Development Skill](.claude/skills/koan-entity-first/SKILL.md)

#### Common Patterns

```csharp
// Retrieve by ID
var todo = await Todo.Get(id);
var todos = await Todo.GetAll();
var results = await Todo.Query(x => x.IsComplete == false);

// Create
var newTodo = new Todo { Title = "Buy milk" };
await newTodo.Save();

// Update
var todo = await Todo.Get(id);
todo.Title = "Buy organic milk";
await todo.Save();

// Delete
await todo.Delete();
```

#### When to Use
- Application-level entity operations
- Avoiding manual repository injection
- Following Koan's "Reference = Intent" pattern
- Rapid prototyping and sample code

---

## Common Patterns

### Guard Clauses

**Location**: `src/Koan.Core/Utilities/Guard/`
**Pattern**: Static validation methods

#### Available Guards

```csharp
Must.NotBeNull(value, nameof(value));
Must.NotBeEmpty(collection, nameof(collection));
Must.BeInRange(value, min, max, nameof(value));
Must.BeOfType<T>(obj, nameof(obj));

Be.Positive(value, nameof(value));
Be.ValidEmail(email, nameof(email));
Be.ValidUrl(url, nameof(url));

NotBe.Negative(value, nameof(value));
NotBe.Default(value, nameof(value));
```

#### Usage Example

```csharp
public class MongoRepository<T>
{
    public MongoRepository(string connectionString, string database)
    {
        Must.NotBeNull(connectionString, nameof(connectionString));
        Must.NotBeEmpty(database, nameof(database));

        _connectionString = connectionString;
        _database = database;
    }
}
```

#### When to Use
- Constructor validation
- Method parameter validation
- Public API boundary enforcement
- Defensive programming

---

### Provenance Helpers

**Location**: `src/Koan.Core/Hosting/Bootstrap/ProvenanceModuleExtensions.cs`
**Pattern**: Extension methods for boot report contributions
**Status**: ⚠️ Planned for refactoring (see [REFACTORING-LEDGER.md](../refactoring/REFACTORING-LEDGER.md) P1.01)

#### Current Pattern (To Be Refactored)

```csharp
// Duplicated across 53 KoanAutoRegistrar files
private static void Publish(ProvenanceModuleWriter writer, string key, object value)
{
    writer.Add(new ProvenanceItem { Key = key, Value = value });
}
```

#### Future Pattern (After P1.01)

```csharp
// Centralized static extension
using Koan.Core.Provenance;

writer.Publish("adapter-name", "Postgres");
writer.Publish("version", "1.0.0");
writer.PublishMultiple(new Dictionary<string, object>
{
    ["host"] = options.Host,
    ["port"] = options.Port,
    ["database"] = options.Database
});
```

#### When to Use
- KoanAutoRegistrar boot reporting
- Diagnostic information contribution
- Service registration metadata

---

## Anti-Patterns to Avoid

### ❌ Don't Duplicate These Utilities

Before creating new helper methods, check if these already exist:

1. **Connection String Parsing** → Use `ConnectionStringParser`
2. **Options Configuration** → Use `OptionsExtensions`
3. **Query Parsing** → Use `EntityQueryParser`
4. **Patch Normalization** → Use `PatchNormalizer`
5. **Guard Clauses** → Use `Must`, `Be`, `NotBe` guards
6. **Discovery Logic** → Inherit from `ServiceDiscoveryAdapterBase`

### ❌ Don't Inject Services for Pure Functions

If a method:
- Has no side effects
- Doesn't need runtime configuration
- Performs pure data transformation
- Is stateless

→ Make it a **static helper** instead of an injected service.

**Example:**
```csharp
// ❌ BAD - Unnecessary service
public interface IConnectionStringParser
{
    string BuildPostgresConnectionString(string host, int port, string database);
}

// ✅ GOOD - Static utility
public static class ConnectionStringParser
{
    public static string BuildPostgresConnectionString(string host, int port, string database) { }
}
```

---

## Decision Framework

**When should I create a new utility vs. using DI?**

See [ARCH-0068: Refactoring Strategy](../decisions/ARCH-0068-refactoring-strategy-static-vs-di.md) for the complete decision framework.

**Quick Reference:**

| Use Static Utility When... | Use DI Service When... |
|----------------------------|------------------------|
| Pure functions (no side effects) | Needs configuration at runtime |
| Zero allocation on hot paths | Has mutable state |
| Used across many assemblies | Requires lifecycle management |
| Testable through inputs only | Needs mock/stub in tests |
| Examples: parsing, validation | Examples: repositories, HTTP clients |

---

## Contributing

**Adding New Utilities:**

1. Check this guide to avoid duplication
2. Follow the decision framework in ARCH-0068
3. Document the utility in this guide
4. Add contextual README if in a new directory
5. Update CLAUDE.md if it affects AI assistant guidance
6. Add unit tests in appropriate test suite

**Questions?**
Refer to [REFACTORING-LEDGER.md](../refactoring/REFACTORING-LEDGER.md) for planned utilities or propose new ones via ADR.

---

**Last Updated**: 2025-11-03
**Maintained By**: Koan Framework Core Team
**Related**: [ARCH-0068](../decisions/ARCH-0068-refactoring-strategy-static-vs-di.md), [Refactoring Ledger](../refactoring/REFACTORING-LEDGER.md)
