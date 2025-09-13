# Working with Entity Data in Sora

This guide centers on the domain-first Entity approach: simple, readable APIs like `Item.Get(id)` and `item.Save()`. Raw `model.Upsert<T,TKey>()` and `Data<TEntity,TKey>` helpers are covered later for advanced control.

Audience: Developers building with Sora’s domain model and generic EntityController.

---

## TL;DR (Entity-first)

- Get one: `var it = await Item.Get(id);`
- Get all: `var all = await Item.All();`
- Query (string filter): `var list = await Item.Query("Name:*milk*");`
- Count: `var n = await Item.Count();` or `await Item.Count("Name:*milk*");`
- Save (string-keyed): `await item.Save();`
- Save to a set (string-keyed): `await item.Save("backup");`
- Remove: `await Item.Remove(id);` or `await item.Remove();`
- Choose a set (ambient): `using (DataSetContext.With("backup")) { await Item.All(); }`
- HTTP filter: `GET /api/items?filter={"Name":"*milk*"}&page=1&size=10`

See 15-entity-filtering-and-query.md for the full filter DSL and pagination headers.

---

## Entity essentials

Sora provides a domain-centric CRTP base `Sora.Domain.Entity<TEntity, TKey>` that exposes static conveniences. Typical usage with an `Item` entity:

```csharp
// Read
var one = await Item.Get(id);
var all = await Item.All();
var filtered = await Item.Query("Name:*milk*"); // string filter DSL

// Write
await Item.UpsertMany(models); // bulk save

// Remove
await Item.Remove(id);
await Item.Remove(new[] { id1, id2 });
await Item.Remove("Status:inactive"); // by string filter
await Item.RemoveAll();

// Instance helpers
await item.Remove(); // delete this instance
```

Note: `Item.Query(..)` uses the same string-query capability that powers `IStringQueryRepository` and the HTTP GET `?q=` when enabled.

---

## Saving instances (friendly verbs)

For string-keyed entities, you can call `item.Save(ct)` directly via extensions. This ensures identifiers and persists changes using the configured repository.

```csharp
await item.Save(); // string-keyed convenience
```

If your key is not a string or you need explicit type control, see “Raw model.\* operations” at the end.

---

## Sets: route the same entity to multiple logical stores

Sora supports logical data “sets” so you can keep multiple parallel collections of the same entity (e.g., root, backup, archive). The physical storage name is resolved per adapter; non-root sets are suffixed internally (e.g., "Todo#backup").

Ways to choose a set:

- Preferred: pass `set` to first-class statics and instance Save("set").
- HTTP: pass `set` in querystring for GET or inside the POST /query body.
- Code: wrap operations in DataSetContext.With("backup"). Root is null/empty.

Example (HTTP):

```text
GET /api/items?filter={"Status":"active"}&set=backup
POST /api/items/query { "filter": {"Status":"active"}, "set": "backup" }
```

Example (code):

```csharp
// Non-ambient (explicit set):
var one = await Item.Get(id, set: "backup");
var countInBackup = await Item.CountAll(set: "backup");
var activeInStaging = await Item.Query("Status:active", set: "staging");

// Save instances to a specific set (string-keyed)
await item.Save("backup");
await moreItems.Save("archive");

// Alternative: ambient set scope
using (DataSetContext.With("backup"))
{
    var count2 = await Item.Count();
}
```

All Entity static methods respect the ambient set. See ADR 0030 for naming and isolation rules.

---

### Sets via REST (?set=)

All standard EntityController endpoints accept a `set` query parameter. Use it to route CRUD operations to a parallel store (backup, archive, staging, etc.). The `POST /query` endpoint continues to accept `set` inside its JSON body.

Typical calls:

```text
# Collection
GET    /api/items?set=backup

# By id
GET    /api/items/{id}?set=backup
PATCH  /api/items/{id}?set=backup
DELETE /api/items/{id}?set=backup

# Upsert
POST   /api/items?set=backup               // body: { ...item }
POST   /api/items/bulk?set=backup          // body: [ { ... }, { ... } ]

# Delete (bulk/all/query)
DELETE /api/items/bulk?set=backup          // body: [ "id1", "id2" ]
DELETE /api/items/all?set=backup
DELETE /api/items?q=Status:inactive&set=backup

# Query via POST keeps set in body
POST   /api/items/query                    // body: { filter: { ... }, set: "backup" }
```

Notes:

- When `set` is omitted, operations target the root collection.
- Pagination and filter semantics are unchanged; only routing differs.

---

## Filtering and pagination: quick recap

- Use wildcards with `*` for starts/ends/contains; equality for exact matches.
- Combine with $and/$or/$not; membership with $in; presence with $exists.
- Case-insensitive matching is via `$options: { "ignoreCase": true }` and provider-friendly lowercasing.
- Server sets pagination headers: X-Total-Count, X-Page, X-Page-Size, X-Total-Pages; Link when applicable.

Full details and examples: 15-entity-filtering-and-query.md.

---

## Advanced: set migrations (clear, copy, move, replace)

Sora provides high-level helpers to manipulate whole sets safely and predictably (batching-friendly). These operations respect adapters and push as much work down as possible.

These live on the data facade `Data<TEntity,TKey>`:

```csharp
// Remove all items from a set
await Data<Item, string>.ClearSet("archive");

// Copy items between sets (optionally filter/map)
await Data<Item, string>.CopySet(
    fromSet: "backup",
    targetSet: "root",
    predicate: e => e.Status == "active",
    map: e => e with { Flagged = true },
    batchSize: 500);

// Move (copy then delete from source)
await Data<Item, string>.MoveSet("backup", "archive");

// Replace target with source (clear target first, then copy)
await Data<Item, string>.ReplaceSet("backup", "root");
```

Fluent builder for complex flows:

```csharp
await Data<Item, string>
    .MoveFrom("backup")
    .Where(e => e.Status == "active")
    .Map(e => e with { IsArchived = true })
    .BatchSize(1000)
    // .Copy() // optional: default is move; call Copy() to keep source
    .To("archive");
```

Instance sugar for ad-hoc moves:

```csharp
await item.MoveToSet<Item, string>(toSet: "backup", fromSet: null /* ambient */, copy: false);
```

Notes:

- ReplaceSet clears the target up-front to guarantee a clean replacement.
- Map lets you transform entities during migration (e.g., stamp flags, rewrite keys).
- BatchSize is advisory; adapters may chunk operations accordingly.

---

## Provider compatibility and gotchas

- Case-insensitive string matching uses one-argument string methods (StartsWith/EndsWith/Contains) over lowercased values to maximize LINQ translation (works with Mongo LINQ and relational providers).
- Equality preserves null semantics; method-based matches coalesce nulls to empty strings so queries remain translatable.
- When introducing a new adapter, ensure its physical naming uses the set suffix for non-root sets and that schema/collections are created on first write for that set.

---

## Recipes

Backup everything from root to backup:

```csharp
await Data<Item, string>.CopySet("root", "backup");
```

Replace root with backup:

```csharp
await Data<Item, string>.ReplaceSet("backup", "root");
```

Archive all active items, marking them:

```csharp
await Data<Item, string>
    .MoveFrom("root")
    .Where(e => e.Status == "active")
    .Map(e => e with { IsArchived = true })
    .To("archive");
```

Run a case-insensitive search via HTTP:

```text
GET /api/items?filter={"$options":{ "ignoreCase": true },"Name":"*report*"}&page=1&size=20
```

---

## Raw model.\* operations (when you need full control)

Most apps can stick to the Entity-first APIs above. If you need to control generic type parameters or work with non-string keys explicitly, use the instance extensions:

```csharp
// Upsert with explicit key type (e.g., Guid)
await model.Upsert<MyEntity, Guid>();

// Or the Save alias with explicit key type
await model.Save<MyEntity, Guid>();

// Return only the identifier
var id = await model.UpsertId<MyEntity, Guid>();
```

Notes:

- For string-keyed entities, prefer `model.Save()` (no type arguments needed).
- These extensions resolve the right repository, ensure identifiers, and persist the model.

### A note on CancellationToken

All APIs shown accept an optional CancellationToken as the last parameter (e.g., `Item.Get(id, ct)`). Omit it for brevity in app code; pass one in long-running operations, background jobs, or when wiring ASP.NET Core request tokens.

---

## Where to go next

See Engineering front door and Reference/Data Access for setup.

- 15-entity-filtering-and-query.md for full filter language details
- docs/decisions for ADRs 0029 (filter language), 0030 (sets), 0031 (ignoreCase)

## Flow: parent relationships and ingestion without canonical IDs

Adapters do not know canonical ULIDs. Declare canonical relationships on models with `[ParentKey]` and let the ingestion resolver fill them using external IDs provided in envelopes or normalized bags. See ADR [FLOW-0105](../../decisions/FLOW-0105-external-id-translation-adapter-identity-and-normalized-payloads.md).

### Declare Parent Relationships

Mark parent properties with `[ParentKey]` so the resolver can bind canonical references:

```csharp
// Example: Sensor → Device parent relationship
public sealed class Sensor : FlowEntity<Sensor>
{
    // Canonical Device ULID (filled by the resolver)
    [Parent(typeof(Device))]
    public string DeviceId { get; set; } = default!;

    [AggregationTag(Keys.Sensor.Key)]
    public string SerialNumber { get; set; } = default!;

    public string Code { get; set; } = default!;
    public string Unit { get; set; } = default!;
}
```

Adapters do not set `DeviceId`. The ingestion pipeline resolves it using external IDs.

### Strong-Typed Ingestion (Envelope External IDs)

Send strong-typed models in bulk and attach external IDs inline with each item. The envelope carries adapter identity automatically.

```csharp
// Adapter code (example)
var batch = new[]
{
    FlowSendItem.Of(
        new Device { Inventory = "INV-123", Serial = "SN-ABC", Manufacturer = "Hitachi", Model = "X1000", Kind = "PLC", Code = "DEV-01" },
        new Dictionary<string,string> { [ExternalSystems.Oem] = "OEM-00001" }),

    FlowSendItem.Of(
        new Sensor { SerialNumber= "INV-123::SN-ABC::TEMP", Code = "TEMP", Unit = "C" },
        // Include parent’s external ID to enable resolver to fill Sensor.DeviceId
        new Dictionary<string,string> { ["oem-device"] = "OEM-00001", ["oem-sensor"] = "S-00987" })
};

// Bulk send; entities remain canonical-only
await _sender.SendBatchAsync(batch, ct);
```

### Contractless ingestion (plain bag, server-stamped)

Prefer sending a simple dictionary bag per item; the server stamps adapter identity and resolves external IDs.

```csharp
var items = new[]
{
    // Device
    FlowSendPlainItem.Of<Device>(
        bag: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [Keys.Device.Inventory] = "INV-123",
            [Keys.Device.Serial] = "SN-ABC",
            [Keys.Device.Manufacturer] = "Hitachi",
            [Keys.Device.Model] = "X1000",
            [Keys.Device.Kind] = "PLC",
            [Keys.Device.Code] = "DEV-01",
            // External/native IDs (identifier.external.*)
            [$"{Constants.Reserved.IdentifierExternal}.oem"] = "OEM-00001",
        },
        sourceId: "events",
        occurredAt: DateTimeOffset.UtcNow),

    // Sensor referencing Device via the same external OEM id
    FlowSendPlainItem.Of<Sensor>(
        bag: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [Keys.Sensor.Key] = "INV-123::SN-ABC::TEMP",
            [Keys.Sensor.Code] = "TEMP",
            [Keys.Sensor.Unit] = "C",
            [$"{Constants.Reserved.Reference}.device"] = new Dictionary<string, object?>
            {
                ["oem"] = "OEM-00001"
            },
            [$"{Constants.Reserved.IdentifierExternal}.oem-sensor"] = "S-00987",
        },
        sourceId: "events",
        occurredAt: DateTimeOffset.UtcNow)
};

await _sender.SendAsync(items, ct: ct);
```

Notes:

- Do not stamp adapter identity in the bag; the ingestion API stamps it based on the host/envelope.
- For large batches, pass IEnumerable<FlowSendPlainItem> to stream or page as needed.

### Resolution and Persistence

- External IDs are indexed as `(entityKey, system, externalId) -> canonicalId`.
- Before persistence, the resolver:
  - Fills `[ParentKey]` properties with canonical IDs.
  - Maps normalized bag fields to model properties.
  - Preserves envelope metadata for audit.

### Notes

- Use centralized constants for systems and reserved keys (see ARCH-0040).
- For large reads of canonical data later, use streaming or paging.

This section shows how to declare canonical relationships and how adapters submit data without knowing canonical IDs. See ADR [FLOW-0105](../../decisions/FLOW-0105-external-id-translation-adapter-identity-and-normalized-payloads.md).

### Declare Parent Relationships

Mark parent properties with `[ParentKey]` so the resolver can bind canonical references:

```csharp
// Example: Sensor → Device parent relationship
public sealed class Sensor : FlowEntity<Sensor>
{
    // Canonical Device ULID (filled by the resolver)
    [Parent(typeof(Device))]
    public string DeviceId { get; set; } = default!;

    [AggregationTag(Keys.Sensor.Key)]
    public string SerialNumber { get; set; } = default!;

    public string Code { get; set; } = default!;
    public string Unit { get; set; } = default!;
}
```

Adapters do not set `DeviceId`. The ingestion pipeline resolves it using external IDs.

### Strong-Typed Ingestion (Envelope External IDs)

Send strong-typed models in bulk and attach external IDs inline with each item. The envelope carries adapter identity automatically.

```csharp
// Adapter code (example)
var batch = new[]
{
    FlowSendItem.Of(
        new Device { Inventory = "INV-123", Serial = "SN-ABC", Manufacturer = "Hitachi", Model = "X1000", Kind = "PLC", Code = "DEV-01" },
        new Dictionary<string,string> { [ExternalSystems.Oem] = "OEM-00001" }),

    FlowSendItem.Of(
        new Sensor { SerialNumber= "INV-123::SN-ABC::TEMP", Code = "TEMP", Unit = "C" },
        // Include parent’s external ID to enable resolver to fill Sensor.DeviceId
        new Dictionary<string,string> { ["oem-device"] = "OEM-00001", ["oem-sensor"] = "S-00987" })
};

// Bulk send; entities remain canonical-only
await _sender.SendBatchAsync(batch, ct);
```

### Contractless ingestion (plain bag, server-stamped)

Prefer sending a simple dictionary bag per item; the server stamps adapter identity and resolves external IDs.

```csharp
var items = new[]
{
    FlowSendPlainItem.Of<Device>(
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [Keys.Device.Inventory] = "INV-123",
            [Keys.Device.Serial] = "SN-ABC",
            [Keys.Device.Manufacturer] = "Hitachi",
            [Keys.Device.Model] = "X1000",
            [Keys.Device.Kind] = "PLC",
            [Keys.Device.Code] = "DEV-01",
            [$"{Constants.Reserved.IdentifierExternal}.oem"] = "OEM-00001",
        },
        sourceId: "events",
        occurredAt: DateTimeOffset.UtcNow),

    FlowSendPlainItem.Of<Sensor>(
        new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [Keys.Sensor.Key] = "INV-123::SN-ABC::TEMP",
            [Keys.Sensor.Code] = "TEMP",
            [Keys.Sensor.Unit] = "C",
            [$"{Constants.Reserved.Reference}.device"] = new Dictionary<string, object?> { ["oem"] = "OEM-00001" },
            [$"{Constants.Reserved.IdentifierExternal}.oem-sensor"] = "S-00987",
        },
        sourceId: "events",
        occurredAt: DateTimeOffset.UtcNow)
};

await _sender.SendAsync(items, ct: ct);
```

Tip: For quick paths, call Send() directly on entities:

```csharp
await new Device { Inventory = "INV-123", Serial = "SN-ABC", Manufacturer = "Hitachi", Model = "X1000", Kind = "PLC", Code = "DEV-01" }
    .Send(sourceId: "events", occurredAt: DateTimeOffset.UtcNow, ct: ct);

await new[]
{
    new Sensor { SerialNumber= "INV-123::SN-ABC::TEMP", Code = "TEMP", Unit = "C" }
}.Send(sourceId: "events", occurredAt: DateTimeOffset.UtcNow, ct: ct);
```

### Resolution and Persistence

- External IDs are indexed as `(entityKey, system, externalId) -> canonicalId`.
- Before persistence, the resolver:
  - Fills `[ParentKey]` properties with canonical IDs.
  - Maps normalized bag fields to model properties.
  - Preserves envelope metadata for audit.

### Notes

- Use centralized constants for systems and reserved keys (see ARCH-0040).
- For large reads of canonical data later, use streaming or paging.
