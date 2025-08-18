# Getting Started with Sora (Beginner-friendly)

Welcome! This guide takes you from zero to productive with Sora. It starts with basics (install, first project), then adds data, web, and progressively advanced scenarios.

Note: This assumes Sora packages are on NuGet (future state). Replace versions as needed.

## 1) Install packages

Quick start (meta packages):
- Sylin.Sora — core runtime + data abstractions + JSON adapter (one package to get started)
- Sylin.Sora.App — Sylin.Sora + Sora.Web for ASP.NET Core apps

Example (PowerShell):
- Console: dotnet add package Sylin.Sora
- Web:     dotnet add package Sylin.Sora.App

Advanced: granular packages (manual composition)
- Core runtime and data abstractions:
    - Sylin.Sora.Core
    - Sylin.Sora.Data.Core
    - Sylin.Sora.Data.Abstractions
- Pick adapters you need (install one or more):
    - Sylin.Sora.Data.Json
    - Sylin.Sora.Data.Sqlite
    - (Future) Sylin.Sora.Data.Relational.SqlServer, Sylin.Sora.Data.Redis, etc.

Example (PowerShell):
- dotnet add package Sylin.Sora.Core
- dotnet add package Sylin.Sora.Data.Core
- dotnet add package Sylin.Sora.Data.Abstractions
- dotnet add package Sylin.Sora.Data.Json
- dotnet add package Sylin.Sora.Data.Sqlite

## 2) Define your entity

Minimal aggregate with an Identifier:

```csharp
public sealed class Todo : Sora.Data.Core.Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
}
```

Alternative: interface-based with explicit attribute:

```csharp
using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;

public sealed class Todo : IEntity<string>
{
    [Identifier]
    public string Id { get; set; } = default!;
    public string Title { get; set; } = string.Empty;
}
```

Concepts:
- Aggregate: your domain type (here, Todo).
- Identity: Entity<T> provides Id and helpers; or use IEntity<string> + [Identifier].
 - Naming note: Repositories and helpers target IEntity<TKey>. Entity<T> is optional sugar that embeds Id and static conveniences.

## 3) Console app (JSON provider)

Quick, no-install storage under a data folder (great for demos/tests). Minimal S0-style:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Data.Core;

var services = new ServiceCollection();

// Optional: if you disable discovery or prefer explicitness, register the JSON adapter:
// services.AddSora().AddJsonAdapter();

services.StartSora(); // boots runtime; JSON adapter is auto-discovered when referenced

var todo = await new Todo { Title = "Learn Sora" }.Save();
var item = await Todo.Get(todo.Id);
var all  = await Todo.All();
Console.WriteLine($"Total items: {all.Count}");
```

What this does:
- Starts Sora with discovery (JSON adapter self-registers when referenced).
- Uses the static facade on Entity<T> (Save/Get/All) to do data operations.
- Persists JSON files under a data/ folder in your app by default.

Concepts introduced:
- Discovery: modules (adapters) can auto-register in dev.
- Repository: behind the scenes, Entity<T> talks to a typed repository.
- Adapter: a storage implementation (JSON, SQLite, etc.).

## 4) Console app (SQLite provider)

SQLite needs a connection string; otherwise usage is identical:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Sora.Core;
using Sora.Data.Core;

var services = new ServiceCollection();
services.AddSora()
    .AddSqliteAdapter(o => o.ConnectionString = "Data Source=./todos.db");
services.StartSora();

var todo = await new Todo { Title = "with SQLite" }.Save();
var all  = await Todo.All();
Console.WriteLine($"Total items: {all.Count}");
```

Notes:
- Provider selection: if multiple adapters are present, Sora picks the highest-priority one.
- To force SQLite for an entity:

```csharp
using Sora.Data.Abstractions.Annotations;
[DataAdapter("sqlite")]
public sealed class Todo : Sora.Data.Core.Entity<Todo> { public string Title { get; set; } = string.Empty; }
```

Concepts introduced:
- Connection string: how relational providers connect.
- Provider priority vs. explicit [DataAdapter] selection.

Named connections with [DataSource]

```csharp
using Sora.Data.Abstractions.Annotations;

[DataSource("reporting")] // logical name
public sealed class Report : Sora.Data.Core.Entity<Report> { public string Title { get; set; } = string.Empty; }

// appsettings.json
// {
//   "Sora": { "Data": { "Sources": { "reporting": { "sqlite": { "ConnectionString": "Data Source=./data/reporting.db" } } } } }
// }
```
Notes:
- Resolution looks under Sora:Data:Sources:{name}:{provider}:ConnectionString, then ConnectionStrings:{name}.
- Falls back to the provider’s default when not set.

Multiple databases with the same provider

Use named data sources to point different entity sets to different databases of the same technology (e.g., two SQLite files):

```csharp
using Sora.Data.Abstractions.Annotations;

[DataSource("write")] // goes to sqlite-write
public sealed class Product : Sora.Data.Core.Entity<Product> { /* ... */ }

[DataSource("read")]  // goes to sqlite-read
public sealed class Activity : Sora.Data.Core.Entity<Activity> { /* ... */ }
```

appsettings.json (two named sources for sqlite):

```json
{
    "Sora": {
        "Data": {
            "Sources": {
                "write": { "sqlite": { "ConnectionString": "Data Source=./data/write.db" } },
                "read":  { "sqlite": { "ConnectionString": "Data Source=./data/read.db" } }
            }
        }
    }
}
```

Or, using root ConnectionStrings (same outcome):

```json
{
    "ConnectionStrings": {
        "write": "Data Source=./data/write.db",
        "read":  "Data Source=./data/read.db"
    }
}
```

The resolver precedence is:
1) `Sora:Data:Sources:{name}:{provider}:ConnectionString`
2) `ConnectionStrings:{name}`
3) Provider default (if any)

## 5) Web API (minimal ASP.NET Core)

Create a new ASP.NET Core app. Add Sora, then register adapters and Sora.Web.

Program.cs
```csharp
using Sora.Data.Core;
using Sora.Web;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora()
    .AddSqliteAdapter(o => o.ConnectionString = builder.Configuration.GetConnectionString("sqlite") ?? "Data Source=./todos.db")
    .AddSoraWeb(); // maps Sora controllers and hosting

var app = builder.Build();
app.UseStaticFiles();
app.MapControllers();
app.Run();
```

appsettings.json (optional configuration)
- Bind `SoraWebOptions` from configuration to tune secure headers, static files, controller mapping, and health path
- Override logging for Sora namespaces

```json
{
    "Sora": {
        "Web": {
            "AutoUse": true,
            "EnableStaticFiles": true,
            "AutoMapControllers": true,
            "EnableSecureHeaders": true,
            "ContentSecurityPolicy": "default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'",
            "HealthPath": "/api/health"
        }
    },
    "Logging": {
        "LogLevel": {
            "Default": "Information",
            "Microsoft": "Warning",
            "Sora": "Information",
            "Sora.Web": "Information",
            "Sora.Data": "Information"
        }
    },
    "ConnectionStrings": {
        "sqlite": "Data Source=./data/s1.sqlite"
    }
}
```

To bind the options in code:

```csharp
// After builder creation
builder.Services.AddSora().AddSqliteAdapter(o =>
        o.ConnectionString = builder.Configuration.GetConnectionString("sqlite")!);

// AddSoraWeb registers options; bind them from configuration
builder.Services.AddSoraWeb();
builder.Services.Configure<SoraWebOptions>(builder.Configuration.GetSection("Sora:Web"));
```

Web pipeline templates and toggles

```csharp
// Sensible defaults for APIs: controllers, static files, secure headers, ProblemDetails
builder.Services.AddSora().AsWebApi();

// Compose feature toggles
builder.Services.WithExceptionHandler(); // enables app.UseExceptionHandler()
builder.Services.WithRateLimit();        // enables app.UseRateLimiter(); register limiter in your app

// Example rate limiter registration stays in your app (libraries don’t add dependencies):
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 200, Window = TimeSpan.FromMinutes(1) }));
});
```

Notes:
- Rate limiter registration (AddRateLimiter) remains in your app. The Web toggle only wires UseRateLimiter().
- You can also configure `Sora:Web` and `Sora:WebPipeline` from appsettings to tweak behavior.
 - Health endpoints:
     - `/api/health` (controller) returns `{ status: "ok" }`.
     - `/health/live` (liveness) always healthy unless process is failing.
     - `/health/ready` (readiness) aggregates IHealthContributor; sets 503 when Unhealthy.
     - `Sora:Web:HealthPath` can expose an additional lightweight health path; to avoid conflict, it won’t override `/api/health`.
    - Runtime health announcements: one-liners for degraded/unhealthy signals with TTL
        ```csharp
        using Sora.Core;
        HealthReporter.Degraded("sqlite", "slow writes", ttl: TimeSpan.FromMinutes(2));
        HealthReporter.Healthy("sqlite"); // clears the message
        ```
        - Provider health contributors (pull checks):
                - JSON: verifies the data directory exists and is writable.
                    - Configure directory via `Sora:Data:Json:DirectoryPath` or `Sora:Data:Sources:Default:json:DirectoryPath`.
                - SQLite: opens a connection and runs a PRAGMA.
                    - Configure connection via `Sora:Data:Sqlite:ConnectionString`, `Sora:Data:Sources:Default:sqlite:ConnectionString`, or `ConnectionStrings:Default`.
                - Both contributors are auto-registered when adapters are present; they are marked critical and will flip readiness to 503 on failures.

Minimal S1-style Program.cs using templates

```csharp
using Sora.Data.Core;
using Sora.Web;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSora()
    .AsWebApi()
    .WithExceptionHandler()
    .WithRateLimit();

builder.Services.AddRateLimiter(o =>
{
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = 200, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();
app.Run();
```

Controller
```csharp
using Microsoft.AspNetCore.Mvc;
using Sora.Web.Controllers;
using Sora.Data.Abstractions.Annotations;

[Route("api/todo")]
[SoraDataBehavior(MustPaginate = true, DefaultPageSize = 10)]
public sealed class TodoController : EntityController<Todo> { }
```

Entity (forces sqlite)
```csharp
[DataAdapter("sqlite")]
public sealed class Todo : Sora.Data.Core.Entity<Todo>
{
    public string Title { get; set; } = string.Empty;
}
```

What this does:
- Adds Sora.Web to auto-map controllers (health, discovery, entity controllers).
- Exposes discovery at /.well-known/sora/capabilities.
- Uses EntityController<T> for CRUD with paging behavior via [SoraDataBehavior].

Concepts introduced:
- Hosting integration: minimal setup to get endpoints.
- Behavior attributes: adjust defaults like pagination and page size.

## 6) Querying data

- Get by id: await repo.GetAsync(id)
- List all: await repo.QueryAsync((object?)null)
- String query (relational): await srepo.QueryAsync("Title LIKE @p", new { p = "%milk%" })
- LINQ predicate (when supported): await lrepo.QueryAsync(x => x.Title.Contains("milk"))

Detect capabilities:

```csharp
if (repo is IQueryCapabilities qc) {
    // qc.Capabilities has flags: String, Linq
}
if (repo is IWriteCapabilities wc) {
    // wc.Writes has flags: BulkUpsert, BulkDelete, AtomicBatch
}
```

Notes:
- Relational providers parameterize string queries safely.
- LINQ pushdown is supported for simple predicates; otherwise it falls back to in-memory filtering.
 - Pagination: totals are computed via `CountAsync` when available; if the server slices pages in memory, responses include `Sora-InMemory-Paging: true`.
 - Observability: call `AddSoraObservability()` to enable OpenTelemetry via config/env. When tracing is active, responses include `Sora-Trace-Id` for correlation.
 - Observability snapshot: check `/.well-known/sora/observability` for a safe status JSON (enabled by default in Development; off in Production unless `Sora:Web:ExposeObservabilitySnapshot=true`).

## 7) Batching

```csharp
var batch = repo.CreateBatch();
batch.Add(new Todo { Title = "a" })
    .Update(id, t => t.Title = "b")
    .Delete(id2);
var result = await batch.SaveAsync(); // returns adds/updates/deletes counts
```

Notes:
- Some providers apply batches atomically; others execute per-operation.
- Prefer batch for fewer roundtrips and optional bulk support.
 - For entity/list helpers naming, see ADR 0016.

Examples (entity/list helpers):

```csharp
// Single entity
await todo.Save(ct);              // string-key alias
await todo.Upsert<Todo, Guid>(ct);
var id = await todo.UpsertId<Todo, Guid>(ct);

// List of entities
await items.Save<Todo, Guid>(ct);
await items.SaveReplacing<Todo>(ct); // clears then inserts

// Batch conversions
```

## 8) Optional: Implicit CQRS (profiles + outbox)

Zero-boilerplate CQRS records generic change events and mirrors writes to a read model of the same shape, driven by profiles.

- Turn on via configuration under `Sora:Cqrs:Profiles` and mark aggregates with `[Sora.Data.Cqrs.Cqrs]` or `[Cqrs("ProfileName")]`.
- Each profile maps entities to a Write endpoint and a Read endpoint (provider + connection/source).
- The default outbox is in-memory; replace `IOutboxStore` with a durable store for production.

What it does:
- Repository writes append `OutboxEntry` records (Upsert/Delete) with a JSON snapshot.
- A background processor drains the outbox and performs a 1:1 projection into the read repository resolved by the active profile.

Switching outbox providers (discovery + priority)

- Outbox providers register an `IOutboxStoreFactory` with a `ProviderPriority`.
- The runtime selects the highest-priority factory automatically.
- Referencing the Mongo outbox package contributes its factory; no extra code needed.

Using a durable Mongo outbox

// in your service setup
// Either just reference Sora.Data.Cqrs.Outbox.Mongo (factory is discovered), or call:
services.AddMongoOutbox(); // binds from Sora:Cqrs:Outbox:Mongo; defaults name to "mongo"

Options and connection resolution:
- Bound from Sora:Cqrs:Outbox:Mongo
- ConnectionString resolution order:
    1) options.ConnectionString
    2) Sora:Data:Sources:{name}:mongo:ConnectionString (name from ConnectionStringName, default "mongo")
    3) ConnectionStrings:{name}
- Other options: Database ("sora"), Collection ("Outbox"), LeaseSeconds (30), MaxAttempts (10)

Tip: Configure read/write endpoints per entity in `Sora:Cqrs:Profiles`. The processor picks up whatever `IOutboxStore` is registered.
```

## 8) Attributes & storage mapping

- [Identifier]: mark the identity property when not using Entity<T> base
- [Storage(Name = "Table", Namespace = "app")]: override table name/namespace (relational)
- [StorageName("Column")]: override column name for a property
- [Index(...)] / [UniqueIndex(...)]: define implicit or named indexes
- [IgnoreStorage]: skip persisting a property

## 9) Choosing providers

- Default provider selection is by highest ProviderPriority on factories, then type name.
- Override per-entity with [DataAdapter("provider")].
- Multiple providers can live side-by-side; each entity can choose its own.

Naming conventions (defaults and overrides)
- Explicit mapping wins: use [Storage(Name, Namespace)] or [StorageName] on properties.
- Per-entity hint: [StorageNaming(FullNamespace|EntityType)].
- Adapter defaults:
    - Mongo: FullNamespace with "." separator (e.g., My.App.Todo)
    - Relational (SQLite): EntityType with optional casing; FullNamespace composes parts via "_" if enabled

Quick overrides without custom classes
- Global override delegate:
    - services.OverrideStorageNaming((type, conv) => /* return name or null to use default */);
- Global fallback defaults (used when no provider defaults are registered):
    - services.ConfigureGlobalNamingFallback(o => { o.Style = EntityType; o.Separator = "_"; o.Casing = Lower; });

## 10) Multi-database scenarios

- Use different providers for different aggregates (e.g., SQLite for Todo, Redis for CacheEntry).
- For a single aggregate across providers, implement separate entity types or repositories; Sora doesn’t multiplex a single entity to multiple providers automatically.
- Advanced: write a custom adapter that delegates to different backends based on tenant/org/runtime flags.

## 11) Instructions (advanced)

For provider-specific power features without coupling controllers, use instructions:

```csharp
var exec = repo as IInstructionExecutor<Todo>;
var ok = await exec!.ExecuteAsync<bool>(new Instruction("relational.schema.ensureCreated"));
```

Concepts:
- Instruction: named command understood by a provider (e.g., ensure schema).
- Escape hatch: power without binding app code to a specific provider API.

## 12) Diagnostics (dev only)

Need to see configured entities and caches?

```csharp
// If you don't have a ServiceProvider yet:
// var sp = new ServiceCollection().StartSora();

var diag = sp.GetRequiredService<IDataDiagnostics>();
var snapshot = diag.GetEntityConfigsSnapshot();
foreach (var e in snapshot)
{
    Console.WriteLine($"{e.EntityType} => Provider={e.Provider}, Id={e.IdProperty}");
    foreach (var bag in e.Bags) Console.WriteLine($"  Bag: {bag.Key} => {bag.Type}");
}
```

Notes:
- Diagnostics are read-only and intended for development.

## 13) Tips

- Prefer string queries for large relational datasets when LINQ pushdown isn’t available.
- Use bulk methods (UpsertMany/DeleteMany/Batch) when supported by provider.
- In dev, Sora can auto-discover adapters; in prod, prefer explicit registrations.

## 14) What’s next?

- Explore docs/04-adapter-authoring-guide.md for building your own adapter
- Read docs/decisions for key architecture trade-offs
- Try the samples in the repo (S0.ConsoleJsonRepo, S1.Web)
