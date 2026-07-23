I’d migrate this incrementally, keeping the public and operational contracts fixed while replacing the internals one slice at a time.

The application intent is: users create, inspect, update, and delete reminders through the same HTTP contract, and due reminders are delivered reliably without changing deployment or configuration contracts.

The initial Koan surface would be:

```xml
<PackageReference Include="Sylin.Koan.App" />
<PackageReference Include="Sylin.Koan.Data.Connector.Mongo" />
<PackageReference Include="Sylin.Koan.Jobs" />
```

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddKoan();

var app = builder.Build();

// Keep the existing middleware, authentication, filters, and route setup.
await app.RunAsync();
```

Koan 0.20 targets `net10.0`, so if the app is older, I would first upgrade the runtime and prove the unchanged API before introducing Koan behavior.

The migration should proceed as follows:

1. Freeze the current contract with tests covering every route and verb, request/response JSON, status code, headers, validation/problem details, authentication behavior, pagination, and environment key. Record the exact Mongo database, collection names, `_id` representation, BSON field names, indexes, TTL rules, and null/date serialization.

2. Add `AddKoan()` and the package references without removing any existing controller, repository, hosted service, middleware, or deployment resource. Boot the real composed host and verify startup facts plus `/health/live` and `/health/ready`.

3. Convert the persisted model to an Entity while pinning Mongo explicitly:

```csharp
[DataAdapter("mongo")]
public sealed class Reminder : Entity<Reminder>
{
    public string Text { get; set; } = "";
    public DateTimeOffset DueAt { get; set; }
    public DateTimeOffset? DeliveredAt { get; set; }
}
```

The actual key type and serialization must match the existing Mongo documents; use `Entity<Reminder, TKey>` if the current identifier is not Koan’s default string key. Do not cut over until a provider-level test proves that Koan reads and updates existing documents in the exact existing collection without rewriting their shape.

4. Preserve the handwritten controllers initially. Replace only their repository calls with Entity operations:

```csharp
var reminder = await Reminder.Get(id, ct);
var page = await Reminder.Page(pageNumber, pageSize, ct);
await reminder.Save(ct);
await reminder.Remove(ct);
```

Keep the existing DTOs and explicit mappings so payload names and shapes do not accidentally become persistence details. Keep every existing `[Route]`, `[HttpGet]`, `[HttpPost]`, authorization attribute, response mapping, and error contract. `EntityController<Reminder>` should replace a controller only if its complete HTTP contract is proven byte-for-byte compatible; routine CRUD similarity is not enough.

5. Remove each repository only after all of its consumers use Entity operations and Mongo parity passes. Repository code that contains real application policy should be moved to a domain service or lifecycle rule, not deleted with the plumbing.

6. Replace the hosted poller with durable Koan Jobs. A useful shape is a scheduled singleton sweep that finds due reminders and submits pointwise delivery work:

```csharp
public static class ReminderActions
{
    public const string Deliver = nameof(Deliver);
    public const string Sweep = nameof(Sweep);
}

[DataAdapter("mongo")]
[JobIdempotent(nameof(Id), nameof(DueAt))]
public sealed class Reminder : Entity<Reminder>, IKoanJob<Reminder>
{
    // Existing persisted fields remain unchanged.

    public static async Task Execute(
        Reminder reminder,
        JobContext context,
        CancellationToken ct)
    {
        if (context.Action != ReminderActions.Deliver)
            return;

        if (reminder.DeliveredAt is not null)
            return;

        var sender = context.Services.GetRequiredService<IReminderSender>();
        await sender.Send(reminder, idempotencyKey: $"{reminder.Id}:{reminder.DueAt:O}", ct);

        reminder.DeliveredAt = DateTimeOffset.UtcNow;
    }
}
```

The sweep should use bounded paging or Mongo-backed `QueryStream`, never an unbounded `All()`, and submit `ReminderActions.Deliver` for due items. If the existing polling interval comes from an environment key, bind that same key to a typed `ReminderPollingOptions`; use a boot-triggered sweep that calls `context.Reschedule(options.Interval)` so no deployment key is renamed or hard-coded.

Mark durable work with:

```csharp
[JobPersistence(JobPersistenceMode.DataStore)]
```

That makes missing durable Mongo infrastructure a composition failure instead of silently degrading to an in-memory queue. Jobs provides durable ledger acceptance, restart survival with Mongo, competing consumers across replicas, retries, and at-least-once execution. The application still owns idempotent delivery because no framework can make an external notification exactly-once merely by retrying it.

Preserve the environment contract by translating the existing keys in-process rather than changing manifests:

```csharp
builder.Configuration["ConnectionStrings:Mongo"] =
    builder.Configuration[ExistingKeys.MongoConnection]
    ?? throw new InvalidOperationException(
        $"{ExistingKeys.MongoConnection} is required.");

builder.Configuration["Koan:Data:Mongo:Database"] =
    builder.Configuration[ExistingKeys.MongoDatabase]
    ?? throw new InvalidOperationException(
        $"{ExistingKeys.MongoDatabase} is required.");
```

`ExistingKeys` should contain the app’s real current key names. Secrets remain in the existing platform secret store. Docker Compose, Aspire, or Kubernetes continues to provision the same app and Mongo containers; Koan discovers and monitors them but does not replace the topology.

Before removing the old mechanisms, I would require these proofs:

| Intent | Old owner | New owner | Required proof |
|---|---|---|---|
| HTTP compatibility | Handwritten CRUD plus repositories | Same controllers plus Entity operations | Golden HTTP contract tests |
| Existing Mongo data | Repository/driver setup | Koan Mongo connector | Read/update/delete against a production-shaped clone, including key and collection mapping |
| Polling and delivery | Hosted service | Koan Jobs plus application sender | Due selection, restart recovery, retry, duplicate execution, and multi-replica tests |
| Configuration | Existing environment keys | Application aliasing into Koan config | Boot using unchanged manifests and key names |
| Deployment | Existing Compose/Aspire/Kubernetes | Unchanged | Same services, ports, volumes, networks, and replica behavior |

Two issues must fail the migration safely rather than being guessed around:

- If Koan’s public Mongo configuration cannot pin the current collection/key representation exactly, keep the repository for that entity and reduce the mismatch to a focused framework reproduction.
- Durable Jobs will add its ledger and supporting indexes/TTL data to Mongo. If the existing database is not allowed to gain framework-owned collections, decide explicitly whether the ledger may use a separate named Mongo source or whether the hosted poller must remain.

Only after all four boundaries pass would I delete the repository registrations, Mongo driver bootstrap, hosted poller, and their duplicate health checks.
