# CQRS in Sora — for humans

This tutorial explains, in simple terms, how CQRS fits into Sora without changing how you work with your entities. You can keep using `Entity<TEntity, TKey>` and `IEntity<TKey>` as before. We simply add a thin routing layer later in the pipeline (before the storage adapter) to support commands, events, and read models.

## What changes (and what doesn’t)

- What stays the same
  - Your domain types still implement `IEntity<TKey>`.
  - CRUD helpers like `model.Save(ct)`, `collection.SaveReplacing(ct)`, and `Item.RemoveAll(ct)` still work.
  - Controllers can keep inheriting from `EntityController<T>`, which already does pagination headers, ProblemDetails, etc.

- What we add
  - Commands and command handlers ("do something" intents, like CreateProduct).
  - An Outbox to safely record domain events inside the same unit of work as your write.
  - A publisher that ships those events to a message bus (RabbitMQ).
  - Event handlers that update read models (often in a different database).
  - Optional: a query/read model that your GET endpoints can read from.

Think of it as inserting a “switchboard” right before we talk to the database. Reads go straight to the read repository. Writes go through commands that update the write repository and record events to the Outbox.

## Where it plugs in (the pipeline)

Sora’s data flow has three logical steps:

1) Controller layer
   - Accepts HTTP requests, does validation/binding, and calls into the data layer.

2) Routing layer (CQRS)
   - New layer we add. For writes, it accepts Commands and invokes Command Handlers.
   - The handler performs the mutation using the same repositories you already use.
   - Before returning, it appends an Event into the Outbox in the same transaction.

3) Storage adapter
   - Your configured adapter (Sqlite, Mongo, etc.) persists the data. No change here.

Separately, in the background:
- Outbox Publisher reads events from the Outbox table/collection and publishes to RabbitMQ.
- Event Consumers subscribe to RabbitMQ and run Event Handlers that update read models.

This keeps your existing entity usage intact and pushes CQRS routing just before storage.

## Minimal building blocks

- ICommand and ICommandHandler<TCommand>
- IEvent and IEventHandler<TEvent>
- ICommandBus (send), IEventBus (publish/subscribe)
- IOutboxStore and a hosted OutboxPublisher
- Optional: IInboxStore for consumer-side de-duplication

You’ll register these via `services.AddSora().AddMessaging().AddOutbox().AddRabbitMq().AddProjections()` in S3.

## Step‑by‑step: implement one command end-to-end

Example: Create a product and produce a ProductCreated event that updates an Activity feed in Mongo.

1) Define the command
```csharp
public sealed record CreateProduct(string Name, string Sku, decimal Price) : ICommand;
```

2) Implement the handler
```csharp
public sealed class CreateProductHandler : ICommandHandler<CreateProduct>
{
    private readonly IDataRepository<Product,string> _products;
    private readonly IOutboxStore _outbox;
    public CreateProductHandler(IDataService data, IOutboxStore outbox)
    {
        _products = data.GetRepository<Product,string>();
        _outbox = outbox;
    }

    public async Task Handle(CreateProduct cmd, CancellationToken ct)
    {
        var entity = new Product { Name = cmd.Name, Sku = cmd.Sku, Price = cmd.Price };
        await _products.UpsertAsync(entity, ct);
        await _outbox.AppendAsync(new ProductCreated(entity.Id, entity.Name, entity.Sku), ct);
    }
}
```

3) Publish from the Outbox (background)
```csharp
public sealed class OutboxPublisher : BackgroundService
{
    private readonly IOutboxStore _outbox; private readonly IEventBus _bus;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var batch = await _outbox.DequeueAsync(max: 100, stoppingToken);
            foreach (var evt in batch)
            {
                await _bus.Publish(evt, stoppingToken);
                await _outbox.MarkPublishedAsync(evt, stoppingToken);
            }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
```

4) Handle the event and project to Mongo
```csharp
public sealed class ProductCreatedHandler : IEventHandler<ProductCreated>
{
    private readonly IDataRepository<Activity,string> _activity;
    public ProductCreatedHandler(IDataService data) => _activity = data.GetRepository<Activity,string>();

    public Task Handle(ProductCreated evt, CancellationToken ct)
        => _activity.UpsertManyAsync(new[]{ new Activity
        {
            Type = "ProductCreated",
            ProductId = evt.ProductId,
            Payload = new { evt.Name, evt.Sku }
        }}, ct);
}
```

5) Use it from a controller
```csharp
[HttpPost("/api/products")]
public Task<IActionResult> Create([FromBody] CreateProduct body, [FromServices] ICommandBus bus, CancellationToken ct)
    => bus.Send(body, ct).ContinueWith(_ => Results.Accepted("/api/products"), ct);
```

Reads can query either the write store (Sqlite) or the read store (Mongo) depending on your choice. For S3 we’ll point the Activity feed to Mongo and Products to Sqlite.

## How to use it (as a developer)

- I want to write something → Send a Command to the CommandBus.
- I want to react to a domain change → Handle an Event from the EventBus.
- I want to read data fast → Query a read model repository directly.
- I need idempotency → Add a DedupKey to outbox entries and keep an Inbox for consumers.

## Constraints honored

- Entity/IEntity usage stays the same — command handlers still call the same repositories/extensions.
- Routing takes place later in the pipeline (inside handlers) — before the adapter writes happen.

## Feedback and tips

- Start small: one command and one event. Keep CRUD endpoints for everything else initially.
- Eventual consistency: the Activity list may lag for a moment after you create a product.
- Transactions: prefer a transactional outbox (same DB) for Sqlite; for Mongo, use a collection outbox and a session/transaction when available.
- Testing: use Testcontainers (RabbitMQ, Mongo) for integration; keep an in-memory bus for unit tests.
- Troubleshooting: add health contributors for RabbitMQ and Outbox; enable dev logging with redaction (already centralized in Sora).

That’s it. You can layer CQRS without rewriting your domain or controllers. As you get comfortable, move more writes to commands and more reads to dedicated read models.

## Implicit CQRS mode (zero boilerplate)

If you want CQRS with almost no new code, Sora can offer an “implicit” mode:

- Decorate an entity with `[Cqrs]` (conceptual) and optionally `[DataSource("read")]` on the read model type.
- Keep using `Save/SaveReplacing/Remove` as today; no `ICommandHandler`, no custom `OutboxPublisher`.

What the framework does under the hood
- Repo decorator wraps the entity’s repository for writes and will:
    - Perform the underlying write to the configured write source.
    - Append a generic event to the Outbox (e.g., `EntityUpserted<TEntity>` or `EntityDeleted<TEntity>`) including a serialized snapshot and metadata (id, version/timestamp, correlation).
    - Optionally mirror to a read source by projecting the same entity shape (1:1) into the read repository (async via outbox) when `[Cqrs(MirrorRead=true, ReadSource="read", ReadProvider="mongo")]` is configured.
- A built‑in hosted publisher reads the Outbox and publishes to the configured bus (RabbitMQ if available, else in‑memory). You don’t author a publisher.

Configuration (minimal)
- Enable via profile or a single switch: `Sora:Cqrs:Enabled=true`.
- Connection strings remain under root `ConnectionStrings` or `Sora:Data:Sources` (see sections above).

What you gain
- Zero custom handlers for basic CRUD flows; events flow out; simple read model mirroring works out of the box.

Trade‑offs to be aware of
- Events are generic, not domain‑specific. If/when you need rich, intentful events, move that entity to explicit handlers.
- Projections are 1:1 mirrors (same shape). Complex read models still need explicit handlers.
- Transactional guarantees depend on the adapter: relational outbox is transactional; Mongo outbox is best‑effort unless transactions are available.
- Ordering and idempotency use built‑in keys (entity id + version/timestamp). Cross‑aggregate causality still needs explicit modeling when required.

Pragmatic path
- Start implicit for 80% cases (simple CRUD + basic feed/search). Promote hot spots to explicit commands/events later without changing your entity/controller usage.

## Configuration: where do connection strings go?

- Keep connection strings at the root `ConnectionStrings` section (ASP.NET Core convention). CQRS config lives under `Sora:Cqrs` (or profiles), but it doesn’t replace or move your connection strings.
- Sora resolves connection strings in this order (see Getting Started for details):
    1) `Sora:Data:Sources:{name}:{provider}:ConnectionString`
    2) `Sora:Data:{provider}:ConnectionString`
    3) `ConnectionStrings:{name}` (and commonly `ConnectionStrings:Default`)

Minimal example (write in Sqlite, read in Mongo; profile enabling CQRS):

```json
{
    "Sora": {
        "Profiles": { "Active": "Distributed" },
        "Cqrs": {
            "Enabled": true
            // other CQRS knobs optional; defaults apply
        }
    },
    "ConnectionStrings": {
        "sqlite": "Data Source=./data/app.db",
        "Mongo": "mongodb://localhost:27017"
    }
}
```

Tip: If you use named data sources (e.g., `[DataSource("read")]`, `[DataSource("write")]`), you can either specify provider-specific connection strings under `Sora:Data:Sources` or reuse the root names via `ConnectionStrings:read` and `ConnectionStrings:write`.

Two databases, same provider (read/write split)

- Mark entities with different `[DataSource]` names and configure both sources under `Sora:Data:Sources` or via root `ConnectionStrings`:

```json
{
    "Sora": { "Data": { "Sources": {
        "write": { "sqlite": { "ConnectionString": "Data Source=./data/write.db" } },
        "read":  { "sqlite": { "ConnectionString": "Data Source=./data/read.db" } }
    } } }
}
```

This works even without CQRS; CQRS then routes writes through commands (hitting the write source) and reads to the read source.
