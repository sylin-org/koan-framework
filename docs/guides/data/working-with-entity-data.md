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

If your key is not a string or you need explicit type control, see “Raw model.* operations” at the end.

---

## Sets: route the same entity to multiple logical stores

Sora supports logical data “sets” so you can keep multiple parallel collections of the same entity (e.g., root, backup, archive). The physical storage name is resolved per adapter; non-root sets are suffixed internally (e.g., "Todo#backup").

Ways to choose a set:
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
var count = await Item.Count("Name:*milk*");
var countInBackup = await Data<Item, string>.CountAllAsync("backup");

// Alternative: ambient set scope
using (DataSetContext.With("backup"))
{
    var count2 = await Item.Count();
}
```

All Entity static methods respect the ambient set. See ADR 0030 for naming and isolation rules.

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

## Raw model.* operations (when you need full control)

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

- 11-getting-started.md for setup and running samples
- 15-entity-filtering-and-query.md for full filter language details
- docs/decisions for ADRs 0029 (filter language), 0030 (sets), 0031 (ignoreCase)
