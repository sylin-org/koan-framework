# Sylin.Koan.Data.Connector.Sqlite

SQLite is Koan's embedded relational reference adapter. It supports the ordinary Entity experience for a managed
store and the same compact source experience for external or legacy files.

## Install

```powershell
dotnet add package Sylin.Koan.Data.Connector.Sqlite
```

For an application-owned store, reference the package and use the normal bootstrap:

```csharp
builder.Services.AddKoan();

var todo = await new Todo { Title = "Ship" }.Save();
var open = await Todo.Query(item => !item.Done, ct);
```

The zero-configuration target is `.koan/data/Koan.sqlite`. SQLite creates it on first elected use; merely loading
the connector does not touch disk. File, private-memory, and named shared-memory connection strings are supported.

## Posture

Every connection the connector opens carries one declared posture, bound from `Koan:Data:Sqlite`:

```json
{
  "Koan": {
    "Data": {
      "Sqlite": {
        "JournalMode": "Wal",
        "BusyTimeout": "00:00:30",
        "Synchronous": "Full"
      }
    }
  }
}
```

`JournalMode` (`Wal`, `Delete`, `Truncate`, `Persist`, `Memory`, `Off`) is set on every writable file database and
stays in the file; an in-memory database and a read-only connection keep the mode they have. `BusyTimeout` is how long
a connection waits for another connection's lock: it bounds SQLite's own wait and the driver's busy retry, rounded up
to whole seconds. `Synchronous` (`Full`, `Normal`, `Extra`, `Off`) is the flush level;
`Normal` trades the last transactions before a power loss for throughput under WAL. The same keys under
`Koan:Data:Sources:Default:sqlite` take precedence. A value outside the choices refuses at boot naming the key. The
startup facts show the three settings; `/health/ready` shows them beside the journal mode each source's database holds.

## Hold a transaction across a scope

`await EntityContext.Hold("name", ct)` begins that native transaction at the start of the scope: reads see the scope's
writes, a write acts when it is issued, and any other writer waits, in another process too, for at most the busy timeout.
Keep it short, since it keeps every other writer out. See
[DATA-0166](../../../docs/decisions/DATA-0166-a-scope-can-hold-a-native-transaction-from-its-start.md).

## Commit several entity types at once

On one SQLite route, `EntityContext.Transaction` commits everything it tracked inside one native transaction, begun
immediate, so the saves and removes of several entity types land together or not at all. A scope that must have that
asks for it, and is refused with the correction on a route that cannot give it:

```csharp
using (EntityContext.Transaction("writer", TransactionGuarantee.Atomic))
{
    await order.Save(ct);
    await line.Save(ct);
    await EntityContext.Commit(ct);   // one BEGIN IMMEDIATE … COMMIT; a failure anywhere leaves nothing durable
}
```

See [DATA-0158](../../../../docs/decisions/DATA-0158-sqlite-posture-and-atomic-commit.md).

## Search declared text

Mark the string properties to search and SQLite keeps an FTS5 index over them, in step with every save and remove
inside the same write unit as the row:

```csharp
public sealed class Post : Entity<Post>
{
    [Lexical] public string Title { get; set; } = "";
    [Lexical] public string Body { get; set; } = "";
}

var matches = await Post.Lexical.Search("copper lantern", s => s.Top(20).Snippet(), ct);
```

Every word is optional and a row holding more of them ranks higher; a snippet marks the matched words. `AsTyped()`
finds the word the text ends in by its start, for a box that answers as someone types, and `Within(post => …)` searches
only the rows a predicate holds for. Rows that
existed before the declaration are indexed by `Post.Lexical.Rebuild(ct)`, and `Post.Lexical.Status(ct)` says whether
the index covers every row. See [DATA-0159](../../../../docs/decisions/DATA-0159-lexical-search-over-declared-entity-text.md).

## Inspect and name useful reads

Use provider-neutral storage vocabulary to see what a source contains:

```csharp
var source = Data.Source("Legacy");
var page = await source.Inspect().Containers(100, ct: ct);
var customer = await source.Inspect().Resolve(StorageAddress.From("CUSTOMER"), ct);
var shape = await source.Inspect().Describe(customer, ct);
var sample = await source.Inspect().Sample(customer, 20, ct);
```

Give reusable SQL a business name in the one application composition call. Opaque SQL requires a configured read
lane; SQLite enforces it with `PRAGMA query_only = ON` on the selected lane connection.

```csharp
builder.Services.AddKoan(koan =>
{
    koan.Data.Source("Legacy").Query("customers.active", query => query
        .Lane("Reports")
        .Sql("select CUSTOMER_NO as Id, DISPLAY_NM as Name from CUSTOMER where ACTIVE = @active")
        .Parameter<bool>("active"));

    koan.Data.Source("Legacy").Scalar<long>("customers.count", query => query
        .Lane("Reports")
        .Sql("select count(*) from CUSTOMER"));
});

var active = await Data.Source("Legacy").Query("customers.active", new { active = true }, ct);
var count = await Data.Source("Legacy").Scalar<long>("customers.count", ct);
```

## Map a legacy shape

One compiled map drives hydration, writes, identity predicates, filters, sorts, batches, and schema validation:

```csharp
builder.Services.AddKoan(koan =>
    koan.Data.Source("Legacy").Map<Customer>(map => map
        .Container("CUSTOMER")
        .Key(customer => customer.Id).Name("CUSTOMER_NO")
        .Property(customer => customer.Name.Full).Name("DISPLAY_NM")
        .Property(customer => customer.Profile).Object("PROFILE_JSON")));

using (EntityContext.Source("Legacy"))
{
    var customer = await Customer.Get(7, ct);
    customer!.Name.Full = "Updated";
    await customer.Save(ct);
}
```

Supported shapes are identity plus whole object, flat physical names, mixed scalar/object values, and logical
properties backed by nested structured paths. Composite and provider-generated identities are supported. Nested
path updates preserve unrelated JSON properties.

Set `StorageLifecycle: External` to prohibit physical shape mutation. Set `Access: ReadOnly` to reject every Entity
write before provider I/O. An explicit map pins its declared container, so combining it with an ambient partition
rejects rather than silently sharing that container.

See [TECHNICAL.md](TECHNICAL.md) for guarantees and limits.

## Limits

Constrained single-item creates cannot replace an existing identity, including one hidden by request visibility.
SQLite requires native identity uniqueness for this operation. Constrained bulk requests containing creates
currently reject before persistence. Ordinary Entity `Save` remains an upsert.

Configuration decides participation; unsupported requests reject before provider work with a named capability and a correction. Provider-specific limits live in the package's TECHNICAL.md.

## Enum storage contract

Default Entity storage preserves enum names as strings, including nullable values, nested values, collections,
and declared EnumMember aliases. Unnamed numeric values fail instead of silently changing the storage format.
Queries use the same spelling. Ordinary enum ordering uses declared ordinal ranks in native expressions while
the stored value stays a string; native ordering of arbitrary Flags combinations rejects correctively.
An explicit external mapping codec remains responsible for its declared physical representation.
Existing numeric rows or columns require a separate, explicit migration; upgrades do not rewrite them automatically.
