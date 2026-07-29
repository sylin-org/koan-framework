---
type: DESIGN
domain: data
title: "Koan Data Adapter Development Primer"
audience: [developers, architects, maintainers, ai-agents]
status: current
last_updated: 2026-07-27
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-27
  status: reviewed
  scope: application ergonomics, adapter authoring workflow, behavioral conformance, and implementation change paths
---

# Koan Data Adapter Development Primer

The job is to let an application express a data decision once, then have the selected adapter realize it faithfully,
quickly, and explainably.

## How to use this primer

This is one artifact with three jobs:

1. **Design contract** — the first examples define the user delight every implementation must preserve.
2. **Development blueprint** — the authoring sequence tells a human or agent how to research, place, implement,
   explain, and prove an adapter through the framework's shared ownership model.
3. **Evaluation and change contract** — the profiles, scenarios, and scorecard identify a behavioral gap, assign it
   to Data, a shared storage-family substrate, or one adapter, and require evidence for remediation or replacement.

Choose the route that matches the work:

| Need | Read in this order |
|---|---|
| Review user delight | §§1–3 |
| Build a new adapter | §1 → §4 → Steps 1–4 in §5 → §§6–9 → Steps 5–10 in §5 |
| Audit an adapter | §8 → §9 → §10; inventory actual behavior before reading implementation |
| Replace an adapter from scratch | §10.6 greenfield boundary → §1 → §4 → Steps 2–10 in §5 → §§6–9; Step 1 connector reuse does not apply |
| Remediate framework gaps | §10; repair shared semantics at the Data/family chokepoint and keep native translation local |

Derive support from the claim manifest and its passing conformance evidence.

**Must** and **required** identify conformance obligations. Core obligations apply to every Data adapter. Earned
obligations apply when the adapter exposes or announces the corresponding capability. A smaller adapter remains
conformant when its claims are truthful and unsupported paths reject correctively.

### The seven laws

1. **Delight before machinery.** Start from the application decision and observable outcome.
2. **One logical contract, native physical realization.** Providers may differ physically; observable Koan
   semantics do not drift.
3. **One owner per concern.** Data owns policy and orchestration, a family substrate owns shared mechanics, and an
   adapter owns only provider translation and execution.
4. **Capability claims are evidence-backed.** Every claim has a real-provider conformance cell; unsupported paths
   reject correctively.
5. **External means no storage-lifecycle mutation.** Explicit shape-changing commands and implicit provider
   auto-create are the same violation; mapped data writes remain a separate `Access` decision.
6. **Performance is behavior.** An optimized-path claim includes active indexes, bounded provider work, and measured
   warm-path cost.
7. **Mapping stops at the aggregate-record boundary.** Relationships, unit of work, change tracking, implicit joins,
   and universal query translation remain application concerns.

## 1. Start with the experience the adapter must make true

Begin with the application contract and support profile.

### 1.1 Explore an external source safely before mapping it

Reference the connector that can reach the source:

```powershell
dotnet add package Sylin.Koan.App
dotnet add package Sylin.Koan.Data.Connector.SqlServer
```

Configure storage-lifecycle authority and data access independently:

```json
{
  "Koan": {
    "Data": {
      "Sources": {
        "LegacyErp": {
          "Adapter": "sqlserver",
          "ConnectionString": "Server=legacy-db;Database=erp;Integrated Security=true;Encrypt=true;ApplicationIntent=ReadOnly",
          "StorageLifecycle": "External",
          "Access": "ReadOnly",
          "ReadLanes": {
            "Reports": {
              "ConnectionString": "<provider-enforced read-only route>"
            }
          }
        }
      }
    }
  }
}
```

`StorageLifecycle: External` says Koan may not create, alter, or drop physical objects. `Access: ReadOnly`
says no Koan path may mutate data. Neither is inferred from the other.

The deployment identity and `Reports` lane must have provider-enforced read authority. In this SQL Server example,
`ApplicationIntent=ReadOnly` is a routing hint; database grants provide the security boundary. `ReadLanes` is a
source-owned policy slot that an adapter may realize with a least-privilege route, a read-only session/transaction,
or a provider read-only invocation mode. It may share
a physical pool only when that native boundary remains true. The lane is not a caller-selectable connection override
and can never execute an effective write.

Exploration is source-centered and uses Koan's routing and storage vocabulary:

```csharp
var legacy = Data.Source("LegacyErp");
var inspector = legacy.Inspect();

var page = await inspector.Containers(take: 100, continuation: null, ct);
var customer = await inspector.Resolve(
    StorageAddress.From("dbo", "CUSTOMER"),
    ct);
var description = await inspector.Describe(customer, ct);
RecordSet sample = await inspector.Sample(customer, take: 20, ct);
```

`customer` is an opaque, source-bound container reference issued by the inspector. `StorageAddress` is a
source-relative lookup value; `Resolve` validates it through the provider and returns the same reference that
`Containers` returns in `page.Containers`. Subsequent calls execute through the reference. Container listing returns
completion plus an opaque continuation, so `take: 100` preserves whether more containers exist.
When an address identifies multiple provider kinds, `Resolve` returns a typed ambiguity with the safe candidates;
an optional provider-kind selector disambiguates without embedding provider syntax in the common address.

The storage vocabulary has these meanings:

| Koan term | Exact job |
|---|---|
| **Source** | Named routing, configuration, and policy boundary. Physical client/pool identity is adapter-owned, so two Sources may share one pool without sharing policy. |
| **Namespace** | Zero or more provider path qualifiers between the source and a container: for example a schema, scope, attached database, or directory. |
| **Storage container** | An addressable record-producing or record-accepting target: table, collection, search/vector index, keyspace, view, alias, or file-backed record store. A provider kind and traits distinguish physical, virtual, read-only, and writable forms. This is distinct from `AxisMode.Container`, which is an isolation decision requiring separate physical containers. |
| **Partition** | Koan's logical parallel set for an Entity and a coordinate in Entity routing. |
| **Particle** | The internal name-composition contribution rendered around the Entity storage-name anchor for a partition or isolation axis. |
| **Moniker** | The source key produced by a Database-mode axis. Explicit application Source selection is an independent routing decision. |

Namespace and Partition occupy different planes. A provider scope can be a physical Namespace in inspection while
also realizing a logical Koan Partition for a particular Entity route. A partition may become `Todo#archive` on
SQLite, a Couchbase scope, or another provider-native isolation primitive.

The common descriptor supports providers with flat, hierarchical, and virtual topologies:

```text
StorageContainerDescriptor
  reference         opaque, source-bound execution identity
  address           ordered namespace segments + local name
  display path      diagnostic only
  provider kind     extensible identity such as table, view, collection, alias, or stream
  intrinsic traits  normalized provider/container facts
  effective ops     source-policy-projected operations
  record shape      optional

StorageContainerPage
  containers         bounded descriptors
  completion         Complete | MoreAvailable | ProviderLimit
  continuation       opaque and source-bound; required for MoreAvailable; optional for resumable ProviderLimit
```

`Complete` requires a null continuation. `MoreAvailable` means the caller's requested page bound was reached and a
continuation is present. `ProviderLimit` means the provider itself truncated discovery; it carries a continuation
only when the provider can resume honestly.

Intrinsic traits such as `Records`, `Physical`, and `Virtual` describe the target; effective operations such as
`Describe`, `Sample`, `Query`, and `Write` are those traits narrowed by source policy and provider permission. A
physically writable container inspected through `Access: ReadOnly` therefore omits effective `Write`. Koan branches on
these normalized facts. `ProviderKind` preserves provider precision for humans and tools while traits communicate
portable behavior.

| Provider | Source selection | Namespace path | Example container kind |
|---|---|---|---|
| Relational | server/database | catalog and/or schema | table or view |
| MongoDB | cluster/database | usually empty | collection or view |
| Couchbase | cluster/bucket | scope | collection |
| Elasticsearch / OpenSearch | cluster/project | optional logical qualifiers | index, alias, or data stream |
| Redis | endpoint/logical database | none by default | no portable container; bounded provider-native inspection only when honestly available |
| JSON/file | configured root | directory segments | file or logical record store |
| Vector store | cluster/project | provider qualifiers | collection, class, or index |

An adapter claims container discovery and sampling only when the provider exposes an honest mechanism. Provider-
specific inspection may expose a richer native topology through an explicit extension.

The sample is bounded, non-mutating, and record-shaped only when the container declares intrinsic `Records` and
effective `Sample`. Relational rows, documents, key/value entries, search documents, and vector records can preserve
nested values in the common record substrate. A provider response that cannot be represented faithfully rejects
the common sample instead of being flattened.

### 1.2 A useful query gets a business name

Register the operation in the application's one composition call:

```csharp
builder.Services.AddKoan(koan =>
{
    koan.Data.Source("LegacyErp").Query(
        "orders.recent",
        query => query
            .Lane("Reports")
            .Sql("""
                select
                    o.ORDER_NO    as OrderId,
                    c.DISPLAY_NM  as CustomerName,
                    o.CREATED_UTC as CreatedAt
                from dbo.ORDERS o
                join dbo.CUSTOMER c on c.CUSTOMER_NO = o.CUSTOMER_NO
                where o.CREATED_UTC >= @since
                order by o.CREATED_UTC desc
                """)
            .Parameter<DateTimeOffset>("since")
            .MaxRecords(500)
            .MaxBytes(4 * 1024 * 1024));

    koan.Data.Source("LegacyErp").Scalar<long>(
        "orders.recent-count",
        query => query
            .Lane("Reports")
            .Sql("select count(*) from dbo.ORDERS where CREATED_UTC >= @since")
            .Parameter<DateTimeOffset>("since"));
});
```

Call it by business intent:

```csharp
var recent = await Data.Source("LegacyErp").Query(
    "orders.recent",
    new { since = DateTimeOffset.UtcNow.AddDays(-7) },
    ct);

var recentCount = await Data.Source("LegacyErp").Scalar<long>(
    "orders.recent-count",
    new { since = DateTimeOffset.UtcNow.AddDays(-7) },
    ct);

foreach (var record in recent.Records)
{
    var id = record.Get<long>("OrderId");
    var customer = record.Get<string>("CustomerName");
}

IReadOnlyList<RecentOrder> typed = recent.Project<RecentOrder>();

if (recent.Completion != RecordSetCompletion.Complete)
{
    // Deliberately narrow the operation or increase an approved bound.
}
```

```csharp
public sealed record RecentOrder(
    long OrderId,
    string CustomerName,
    DateTimeOffset CreatedAt);
```

Compiled DTO projection is the middle path: gain type safety without adopting Entity persistence or granting write
semantics. It supports immutable constructor/positional-record binding as shown, plus writable-property binding. A
missing, duplicate, or incompatible required field rejects with the same corrective conversion rules as direct access.

The operation catalog is application-owned, source-bound, parameterized, inspectable, and immutable after
composition. The stable name is portable application intent; the SQL binding is not. Registered operations are
uncached, are not exposed through REST or MCP, and fail closed under active segmentation unless the application
establishes an explicit host/control-plane scope.

### 1.3 Map a useful external shape to a typed model

The mapping contract is one idea: **logical property path ↔ physical binding**.

```csharp
public sealed class Customer : Entity<Customer, long>
{
    public CustomerName Name { get; set; } = new();
    public CustomerProfile Profile { get; set; } = new();
}

public sealed class CustomerName
{
    public string Full { get; set; } = "";
    public string First { get; set; } = "";
}

public sealed class CustomerProfile
{
    public string? PreferredLanguage { get; set; }
    public IReadOnlyList<string> Tags { get; set; } = [];
}
```

Add the map beside the query/scalar declarations in that same host-owned `AddKoan(koan => ...)` composition callback:

```csharp
koan.Data.Source("LegacyErp").Map<Customer>(map => map
    .Container("dbo", "CUSTOMER")
    .Key(customer => customer.Id).Name("CUSTOMER_NO")
    .Property(customer => customer.Name.Full).Name("DISPLAY_NM")
    .Property(customer => customer.Name.First).Name("FIRST_NM")
    .Property(customer => customer.Profile).Object("PROFILE_JSON"));
```

The fluent grammar separates the application model from its physical realization:

- `Key`, `Property`, and root `Object` select a logical value or role;
- `Container`, `Name`, and `Path` locate its physical realization; and
- `Generated`, `ReadOnly`, and `Codec` add behavior only when the mapping requires it.

The expression has already named the logical property, so `Name` can mean only its physical name. `Object` means one
logical subtree represented by one physical structured value; it does not mean “JSON column.” A document adapter may
realize it as a nested document, while a relational family may realize it as JSON/JSONB or use an explicit codec for
another legacy encoding. The overload accepting a `StorageAddress` remains for programmatic composition, but literal
container segments require no wrapper.

The ordinary Entity vocabulary then returns:

```csharp
using (EntityContext.Source("LegacyErp"))
{
    var customer = await Customer.Get(id, ct);
}
```

Mapping a source to an Entity does not grant write access. Every Entity write rejects before Entity lifecycle
callbacks, storage readiness, transaction creation, or provider I/O while the source is read-only. Enabling writes
requires `Access: ReadWrite`, a complete writable mapping, appropriately privileged write credentials, and
preservation of every independently enforced named-read lane.
`StorageLifecycle: External` can remain unchanged, so Koan writes mapped data but still performs no DDL.

### 1.4 The four physical shapes are one mapping model

Treat whole-object, flat-name, hybrid, and nested-path layouts as physical projections of one logical model.

| Physical shape | Mapping expression | Meaning |
|---|---|---|
| Identity + object | `Key(...).Name("Id")` + `Object("Data")` | One identity value and one complete structured value |
| Flat physical names | `Property(x => x.Name.Full).Name("NAME_FULL")` | A logical path binds to one scalar physical value |
| Hybrid values and objects | scalar `.Name(...)` bindings plus `.Object(...)` for a complex property | Queryable scalars stay separate; a complex subtree stays together |
| Scalar property backed by a nested path | `Property(x => x.NameFull).Path("NAME_DATA", "full")` | A flat POCO property binds inside a physical structured value |

The mapping DSL gives each persisted logical value one authoritative writable binding. A value may instead have an
explicit database-generated or read-only binding. Bidirectional is the default
only when the conversion is reversible. Mapping compilation rejects conflicting authorities and ambiguous paths;
active validation reports missing physical values and unsupported writes before business operations rely on them.
Derived stored projections and index lowering are a separate earned profile. Deployment operations own backfill and
dual-write transitions.

Hydration, writes, filters, sorts, patches, compare-and-set, projection, and index expressions consume the same
compiled binding plan. A source-specific translator qualifies pushdown by logical path, physical binding,
operator, and provider. Symmetric codecs handle legacy encodings such
as `Y/N`, padded strings, enums, and unusual dates. Provider-generated/defaulted values, nullable complex-object
construction, key generation, and required unmapped write values are explicit plan decisions.

Composite identities represent legacy keys directly through immutable application key values and explicit physical
components. Generated identity is declared in the binding. A view or registered read without stable writable identity
projects to a DTO:

```csharp
public readonly record struct CustomerSiteId(long CustomerNo, short SiteNo);

public sealed class CustomerSite : Entity<CustomerSite, CustomerSiteId>
{
    public string DisplayName { get; set; } = "";
}

public sealed class GeneratedCustomer : Entity<GeneratedCustomer, long>
{
    public string DisplayName { get; set; } = "";
}

// Independent composite-identity map.
koan.Data.Source("LegacyErp").Map<CustomerSite>(map => map
    .Key(site => site.Id).Parts(parts => parts
        .Property(key => key.CustomerNo).Name("CUSTOMER_NO")
        .Property(key => key.SiteNo).Name("SITE_NO")));

// Independent database-generated-identity map.
koan.Data.Source("LegacyErp").Map<GeneratedCustomer>(map => map
    .Key(customer => customer.Id).Name("CUSTOMER_NO").Generated());
```

Every composite-key component participates in identity equality, lookup, write predicates, ordering fallbacks, and
codecs; partial or nullable composite identity rejects before execution.

The mapping scope is one aggregate ↔ one provider record. Change tracking, units of work, relationship loading,
implicit joins, schema evolution, and universal LINQ translation belong to application or deployment concerns. A
join-shaped legacy read uses a registered operation or an application-owned view.

### 1.5 Use the ordinary Entity experience with a managed store

Managed sources use the ordinary Entity experience:

```powershell
dotnet add package Sylin.Koan.Data.Connector.Sqlite
```

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

public sealed class Todo : Entity<Todo>
{
    public string Title { get; set; } = "";
    public bool Done { get; set; }
}

var todo = await new Todo { Title = "Ship the adapter" }.Save(ct);
var same = await Todo.Get(todo.Id, ct);
var open = await Todo.Query(item => !item.Done, ct);
await todo.Remove(ct);
```

The connector owns physical translation, provider resources, native operations, and native-failure classification. Data owns the
public projection, commit outcome, retry disposition, lifecycle, and application-facing Entity vocabulary. Native
evidence remains in a restricted diagnostic channel.

### 1.6 Use one portable Vector experience

A source owns routing, policy, and one immutable vector-space contract:

```csharp
koan.Data.Source("Semantic").Vector<Document>(space => space
    .Name("documents")
    .Dimensions(1536)
    .Metric(VectorMetric.Cosine)
    .Visibility(VectorVisibility.Session));
```

Ordinary persistence and retrieval remain direct:

```csharp
await Vector<Document>.Save(document.Id, embedding, new { document.Category }, ct);

VectorPoint<string>? stored = await Vector<Document>.Get(document.Id, ct);
bool deleted = await Vector<Document>.Delete(document.Id, ct);
```

Search uses one compact builder:

```csharp
VectorSearchResult<string> result = await Vector<Document>.Search(
    embedding,
    query => query
        .Top(12)
        .Where(filter)
        .Space("content")
        .AtLeast(.82),
    ct);
```

The complete regular query vocabulary is `Top`, `Where`, `Space`, `AtLeast`, `Text`, `SemanticWeight`, and `After`.
With no clauses, search is pure vector search with `Top(10)`. `SemanticWeight` is valid only with `Text`, is inclusive
`0..1`, and weights the semantic side. Unsupported clauses reject before provider I/O.

The regular result contract is:

```csharp
public sealed record VectorPoint<TKey>(TKey Id, ReadOnlyMemory<float> Embedding, DataObject? Metadata);
public sealed record VectorMatch<TKey>(TKey Id, double Similarity, DataObject? Metadata);
public enum VectorSearchAccuracy { Exact, Approximate }
public sealed record VectorSearchExecution(VectorMetric Metric, VectorSearchAccuracy Accuracy, int? CandidatesConsidered);
public sealed record VectorSearchResult<TKey>(
    IReadOnlyList<VectorMatch<TKey>> Items,
    string? Continuation,
    VectorSearchExecution Execution);
```

`DataObject` is the neutral value algebra from §3. Provider metadata objects do not escape the regular surface.
`Similarity` is finite in `[0,1]` and higher is always closer. Cosine maps its full range; non-negative distance maps
through `1 / (1 + distance)`; unbounded inner product uses a stable logistic transform. The value preserves rank
inside one declared space and is not comparable across spaces, models, metrics, or providers. Provider-native score or
distance is restricted evidence. `AtLeast` uses normalized similarity and rejects when bounded equivalent translation
cannot be proved.

Exact/Approximate execution truth is mandatory. Every result is unique, bounded by `Top`, ordered by descending
`Similarity`, and tie-broken by stable identity. `CandidatesConsidered` is present only when reported honestly. There
is no global total. Continuation exists only when the provider can resume the same source/space/query snapshot safely.

Persistence and visibility are equally explicit:

- `Save` is upsert; the same identity atomically replaces its embedding and metadata.
- `Get(id)` returns one complete point or `null`. `Get(ids)` preserves input count, order, and duplicates, with `null`
  in every missing slot.
- Batch save/delete uses the shared `BatchResult<TKey>` with per-item outcomes. Native bulk cannot weaken guards,
  cancellation, visibility, or isolation.
- `Session` visibility is the default. After an awaited mutation, later operations in that source see it. `Eventual`
  is explicit and `Sync()` is the cancellable visibility barrier for all earlier accepted mutations.
- `Clear()` means semantic delete-all. `Sync()` waits for visibility. The destructive `Flush()` vocabulary is retired.
- `SaveWithVector` is non-atomic orchestration unless one real coordinator proves otherwise. Partial completion throws
  a typed coordination failure with safe entity/vector commit facts and retry or compensation guidance.

Source policy is shared with Entity. Read-only rejects every vector mutation before callbacks, readiness, provider
clients, or storage creation. External rejects create, repair, rebuild, and implicit auto-create; Managed may ensure
only the exact declared shape. Row isolation uses a managed metadata predicate; container/database isolation changes
the physical address. A pinned name that defeats an active axis rejects during plan compilation.

## 2. Registered operations are provider-neutral

Sections 2–3 are required when Registered Reads, inspection samples, or another neutral Record Results surface is in
scope. An Entity-only adapter review may continue at §4 and return here only if it earns those claims.

Koan uses an immutable **operation plan** as the provider-neutral umbrella:

```text
OperationPlan
  source
  name
  effect       Read | Write | SchemaOrAdmin | Unknown
  result       Records | Scalar | Acknowledgement | Native
  delivery     Buffered | Streaming
  parameters
  provider binding
  execution lane (when native effect proof requires one)
  timeout and result bounds
  optional expected shape
```

The axes keep effect, result, and delivery independent. The neutral surface exposes two buffered cells:

```text
Query       Read + Records + Buffered
Scalar<T>   Read + Scalar  + Buffered, exactly one value
```

`Query(name, ...)` registers the record-shaped cell; `Scalar<T>(name, ...)` registers the scalar cell. The result
contract is therefore visible before its binding is configured instead of being changed by a trailing modifier. A
scalar means exactly one provider scalar or one record with exactly one field. Zero records, multiple records, or
multiple fields reject. `ScalarOrDefault<T>`, `Command`, acknowledgement, native envelopes, operation streaming,
schema/administrative effects, arbitrary effect/result combinations, and multi-provider variants within one source
are outside this contract. Ordinary Entity CRUD is Koan's write surface.

Record queries carry all four `RecordSetLimits`. Scalars carry the same `MaxValueBytes` accounting and materialization
duration bound; source defaults apply when the declaration does not narrow them.

The declaration states application intent, and the adapter rejects every structurally detectable contradiction. A
binding has effective effect `Read` only when it is declared read and either the adapter can validate that effect or
the operation is permanently bound to a provider-enforced read-only lane. Opaque bindings always require that lane,
even on a read/write source; widening source access must never make a working registered read unsafe or unavailable.
Otherwise the effect is `Unknown`. `Query` and `Scalar` accept only effective `Read`, on every source. A read-only
credential, connection, transaction, or invocation mode may establish the lane; it is not an exception that permits
`Unknown`. Result kind never authorizes an effect, and Koan never infers safety from `SELECT` or another text prefix.
The lane name resolves during composition inside the source and is frozen into the operation plan; it is not exposed
as a runtime routing argument. Missing or non-enforcing lane configuration fails composition/active validation.

The application-facing call remains the same across capable providers:

```csharp
// Inside AddKoan(koan => ...); the connector supplies the native binding extension.
koan.Data.Source("ProductSearch").Query(
    "products.low-stock",
    query => query
        .Template("products-low-stock-v2")
        .Parameter<int>("threshold")
        .MaxRecords(100));

var lowStock = await Data.Source("ProductSearch")
    .Query("products.low-stock", new { threshold = 5 }, ct);
```

The chain never repeats context. `Query` already establishes a read-shaped record result, so `Lane` does not restate
“read” and a search-owned binding says `Template`, not `SearchTemplate`. Native leaf verbs remain precise where the
payload itself is provider-specific: `Sql`, `Pipeline`, `Template`, and `Function`. Safety axes such as
`MaxRecords`, `MaxBytes`, and timeout stay explicit because they add information rather than ceremony.

An adapter binds a MongoDB pipeline, Redis function, Cosmos stored procedure, Couchbase query, or another native
artifact. It claims named-read capability only when it can establish effective
`Read` and return one buffered record set or scalar. An adapter may unwrap a provider transport/execution envelope
such as a cursor while mapping only normalized count/duration/completion facts into the result and telemetry. A
shard/partition status that proves the business result is partial causes failure unless it is an honest bounded
completion case; raw envelope details remain restricted diagnostics. The adapter may not flatten a business result
envelope, aggregation tree, or heterogeneous result merely to resemble records. A source resolves one provider
binding; an application models provider alternatives as separate sources.

Registration may embed an application-owned provider payload or reference a provider-managed artifact. For an
externally owned source, it never creates, updates, or deletes a function, template, view, stored procedure, or
equivalent provider artifact. Provisioning is a separate storage-lifecycle operation and remains forbidden under
external ownership.

## 3. Use `RecordSet` for neutral results

`RecordSet` preserves ordered data, shape, presence, and completion across relational, document, key/value, search,
vector, and file-backed providers.

The neutral contract is:

```csharp
public sealed record DataField(
    int Ordinal,
    string Name,
    Type? ClrType,
    string? ProviderTypeName,
    bool? IsNullable);

public sealed record DataProperty(string Name, object? Value);

public sealed class DataObject
{
    public IReadOnlyList<DataProperty> Properties { get; }
}

public sealed class DataArray
{
    public IReadOnlyList<object?> Items { get; }
}

public sealed class DataRecord
{
    public object? this[int ordinal] { get; }
    public object? this[string uniqueName] { get; }

    public bool TryGetValue(int ordinal, out object? value);
    public bool TryGetValue(string uniqueName, out object? value);
    public T Get<T>(int ordinal);
    public T Get<T>(string uniqueName);
    public IReadOnlyList<int> FindOrdinals(string name);
}

public sealed record RecordSetLimits(
    int MaxRecords,
    long MaxBytes,
    long MaxValueBytes,
    TimeSpan MaxDuration);

public enum RecordSetCompletion
{
    Complete,
    RecordLimit,
    ByteLimit,
    ValueLimit,
    DurationLimit,
    ProviderLimit
}

public enum RecordSetByteAccounting
{
    MaterializedValueV1
}

public sealed record RecordSetExecution(
    RecordSetLimits EffectiveLimits,
    RecordSetByteAccounting ByteAccounting,
    long AccountedBytes,
    TimeSpan Elapsed);

public sealed class RecordSet
{
    public IReadOnlyList<DataField> Fields { get; }
    public IReadOnlyList<DataRecord> Records { get; }
    public RecordSetCompletion Completion { get; }
    public bool IsComplete => Completion == RecordSetCompletion.Complete;
    public RecordSetExecution Execution { get; }
}
```

`object?` is constrained by a closed neutral value algebra:

- `null`; `bool`; signed/unsigned CLR integers; `float`, `double`, or `decimal`;
- `string`, `byte[]`, or `Guid`;
- `DateOnly`, `TimeOnly`, `DateTime`, `DateTimeOffset`, or `TimeSpan`, preserving the declared CLR temporal kind;
- `DataObject`, whose properties preserve order and duplicate names; or
- `DataArray`, whose items recursively use this same algebra.

Missing exists only as a `DataRecord` presence bit; it is not a sentinel value and cannot appear in an array/object.
JSON/BSON/document values normalize recursively into `DataObject`/`DataArray`; enums normalize to an announced
string/integral binding. A vendor object, lazy cursor, stream, arbitrary POCO, `JsonElement`, or `BsonDocument` is not
neutral and must convert, reject, or remain behind Direct/provider-native inspection. Data owns conversion from this
algebra to `Get<T>` and DTO targets; `ProviderTypeName` retains origin without leaking a provider runtime type.

`DataField` is provider-neutral: relational columns, document fields, key/value entry parts, and search or vector
record fields can all occupy an ordinal. Names need not be unique. Ordinal access is lossless relative to the value
the official provider driver exposes. Name access succeeds only when exactly one field has that name, while
`FindOrdinals` makes duplicates explicit. A string indexer, `Get<T>`, or `TryGetValue` throws typed ambiguity when
multiple fields share the name; `TryGetValue` returns `false` when no field resolves or the one resolved field is
absent in that record.

`RecordSet` has one regular shared shape fixed before the first record is exposed. A record may omit one of those
declared fields, preserving missing versus null, but fields and ordinals never grow while materialization proceeds.
An irregular document response is represented as one nested `document` value or rejects the neutral contract; Koan
does not scan the whole result merely to synthesize a global union.

Missing and null are different. `TryGetValue` returns `false` when no field resolves or it is absent from that record;
it returns `true` with `value == null` for an explicit provider null (`DBNull.Value` normalizes to this case). Indexers and
`Get<T>` reject a missing value. `Get<T>` accepts null only when the requested target can represent null; otherwise
it raises a corrective conversion error naming the field, ordinal, provider type, and target type. A relational row
normally has every declared field present; heterogeneous document records may omit one.

Every buffered result has four positive effective limits. `MaxRecords` counts complete records. `MaxBytes` bounds
the shared shape plus admitted values, and `MaxValueBytes` bounds one field value, including a nested object or
array. Both use Data-owned `MaterializedValueV1` accounting:

| Component | Accounted bytes |
|---|---|
| Shared field descriptor, once | 8 + string cost for `Name` + string cost for non-null `ProviderTypeName` |
| Missing record field | 0 after the shared descriptor |
| Any present value | 1 type/presence tag + its payload below |
| Null / Boolean | 0 / 1 payload bytes |
| Integer / float / double / decimal | its CLR primitive width: 1, 2, 4, 8, or 16 bytes |
| String | 4 + two bytes per UTF-16 code unit |
| Binary | 4 + exact byte length |
| `Guid` | 16 |
| `DateOnly` / `TimeOnly` / `DateTime` / `DateTimeOffset` / `TimeSpan` | 4 / 8 / 8 / 16 / 8 |
| `DataArray` | 4 + each item value cost |
| `DataObject` | 4 + the sum, for each property, of its name string cost + value cost |

“String cost” is 4 + two bytes per UTF-16 code unit. The adapter accumulates this function during its one
native-to-neutral conversion pass; it cannot substitute provider payload size, serialize, or walk the completed result merely
to measure it. The count is deterministic safety accounting, not opaque driver buffers or total CLR heap size.
`MaxDuration` is the materialization budget checked at record boundaries; provider execution timeout and caller
cancellation remain separate failure paths.

Koan never clips a value or returns half a record. When a limit prevents the next complete record from being added,
that record is omitted and `Completion` names the first limiting reason. `ProviderLimit` is used only when the
provider explicitly reports a partial result; an unclassifiable partial response rejects. `Complete` is legal only
after the provider reaches the end of the result and no configured or provider limit stopped it. Caller
cancellation, execution timeout, conversion failure, and provider failure throw; they do not masquerade as a partial
successful `RecordSet`.

Field order, duplicate names, null, missing, binary, temporal, numeric, JSON, and nested values survive. Dictionary
projection is optional and requires an explicit duplicate-name policy. A transport cursor may be consumed while
normalized count, duration, and completion populate `RecordSet`; provider page/cursor identities are not exposed by
the buffered contract. Partial shard/partition failure throws, with raw status restricted. A business-result envelope,
aggregation tree, or heterogeneous reply that cannot be represented faithfully rejects the neutral contract. Native
result envelopes and richer inspection metadata belong to explicit provider-specific surfaces.

Neutral execution accepts exactly one result channel. An additional channel, such as a relational second result
set, causes Koan to dispose the execution and throw a typed `AdditionalResultChannelsNotSupportedException`; it never
returns the first channel as if execution were complete. The application must split the registered read or use a
provider-specific Direct escape hatch where one exists; neutral execution never silently discards additional results.

## 4. Decide what the Data pillar owns before writing provider code

The adapter translates Data plans into native operations, executes them, and reports native outcomes.

| Data pillar owns once | Adapter owns for one provider |
|---|---|
| Entity lifecycle callbacks, guards, transforms, segmentation, and source access policy | Native operations and parameter binding |
| Query splitting, residual evaluation, fallback policy, and final shaping | Translation of the complete pushable filter it receives |
| Compiled logical-to-physical mapping plans | Provider-native names, paths, structured values, identifiers, and materialization |
| Operation catalog, effect gate, parameter validation, and bounds | Provider-native operation binding and execution |
| `RecordSet`, conversion rules, completion accounting, and common diagnostics | Provider type metadata and native response conversion |
| Capability negotiation and corrective rejection | Truthful capability declaration and execution receipts |
| Stable failure taxonomy, safe public errors, commit outcome, retry disposition, facts, health, and participation policy | Native error-code/type mapping, route-specific connectivity probe, restricted evidence, and redaction inputs |

Shared mechanics have shared owners. A reflection walk, mapping cache, record materializer, source-policy check,
retry classifier, or operation registry used by multiple relational adapters belongs in Data or the Relational
substrate. Provider-specific semantics stay in the adapter.

## 5. Follow this authoring sequence

This agent-executable workflow proceeds in order; repository code begins after the contract and provider facts are
defined.

**Whole-adapter `REBUILD` override:** when a ratified work item selects a ground-up replacement in §10.6, Step 1's
connector reuse decision and closest-adapter pattern do not supply the design. Necessity and public continuity are
already decided. The implementation starts from an empty adapter root and the ratified Framework/Family contracts.
The former adapter may supply provider facts, public compatibility decisions, negative lessons, and black-box cases;
its type graph, helpers, control flow, tests, and compatibility branches are not implementation inputs.

### Step 1 — Prove a new adapter is necessary

1. Search the product surface, NuGet, and repository for an existing connector or compatible provider alias.
2. Identify the Data family: relational, document, key/value, search-oriented, or genuinely new.
3. Choose the closest shared substrate. Relational providers should reuse `Koan.Data.Relational`; document and
   key/value providers should first test whether the existing shared stores express their semantics.
4. Record why reuse, a provider alias, or an application-owned SDK integration is insufficient.

**Artifact / exit gate:** a reuse decision naming the intended conformance kind, family substrate, alternatives
checked, and the concrete gap. Stop when an existing conformant connector or ordinary SDK integration already
satisfies it.

### Step 2 — Write the user contract and support profile

Copy the relevant scenarios from §1 into the adapter work item. State:

- the package reference and zero-configuration outcome, if one is honest;
- the explicit source configuration and runtime prerequisite;
- managed versus external storage-lifecycle behavior;
- the Entity operations that work, when Entity Persistence is intended;
- optional inspection, mapping, named-read, streaming, transaction, and bulk claims; and
- the exact corrective failure for every unsupported claim.

Select claims from capabilities the provider can realize and prove. A smaller truthful profile is conformant.

**Artifact / exit gate:** an examples-first user contract plus initial claim manifest. The work does not proceed
until Source Core, conformance kind, claimed profiles, and corrective unsupported outcomes are explicit.

### Step 3 — Probe the real provider

Use official driver documentation and a live, least-privilege instance. Capture:

- identity and key encodings;
- null, numeric, temporal, binary, and JSON behavior;
- identifier quoting, length, case, and reserved words;
- filters, sort order, paging, and count behavior;
- transaction and atomic-batch boundaries;
- shape inspection and storage-lifecycle permissions;
- read-only session support;
- cancellation, timeout, retryable faults, and connection-pool behavior; and
- provider-native registered operations and result shapes.

Record every provider surprise as a focused integration fact with an explicit consequence for the claim manifest.

**Artifact / exit gate:** a version-pinned provider probe ledger with official-source links, least-privilege posture,
observed values, native operations/plans where relevant, and unknowns left unclaimed.

### Step 4 — Declare only capabilities with a conformance cell

Every capability token is co-defined with an objective test. An adapter that declares provider-bounded paging, an
atomic batch, conditional replacement, an isolation mode, or registered reads must pass that capability's test against
a real provider instance.

Unverified support remains unclaimed and rejects correctively.

**Artifact / exit gate:** a claim-to-acceptance-cell matrix. Every claim names its real-store cell and every
unclaimed caller path names its fail-closed fact. Before Step 5, the author has read §§6–9 and bound every claim to
its source-policy, readiness, hot-path, profile, and catalog obligations.

### Step 5 — Build the smallest provider package

The package contains the responsibilities required by its conformance kind and claimed profiles. This tree maps
responsibilities; shared family bases may combine several of them:

```text
<Provider>/
  <Provider>Options.cs              typed provider configuration
  <Provider>OptionsConfigurator.cs  configuration precedence and validation
  <Provider>SourceExecutor.cs       Source Integration native dispatch, when claimed
  <Provider>AdapterFactory.cs       Entity repository creation, only for Entity Persistence
  <Provider>Repository.cs           Entity translation/execution, only for Entity Persistence
  Initialization/
    <Provider>DataModule.cs         the one activation owner
  Discovery/                        optional autonomous endpoint discovery
  README.md                         application setup and honest limits
  TECHNICAL.md                      provider contract and operations
```

Add an inspector, connection factory, dialect, operation binder, health contributor, or lifecycle owner only when
the selected shared substrate does not own it. One connector package contains one concrete `KoanModule`. Activation
is the package reference plus `AddKoan()`; it requires no provider-specific registration call.

Use the [connector workbook](../engineering/adding-a-connector.md) for package mechanics, module discovery,
versioning, and integration-test layout. This primer defines Data behavior.

**Artifact / exit gate:** a responsibility/placement map and compiling package skeleton. Every class has one job,
each shared concern points to its family/Data owner, and the package has one activation owner.

### Step 6 — Compile route and mapping state once

At host composition or first legitimate shape use, compile an immutable plan keyed only by stable structure such
as provider, source, Entity/key type, mapping version, and operation name. The hot path consumes that plan directly.

The warm operation excludes:

- reflection over Entity members;
- DI enumeration or adapter election;
- source-policy recomputation;
- mapping conflict detection;
- capability discovery;
- query-text effect inference; or
- dictionary-to-JSON-to-object materialization.

Pool native clients at the narrow physical-source owner. The provider's physical connection/pool identity determines
whether named sources share a pool. Host disposal releases host-owned keepers, clients, active requests,
results/cursors/streams, and transactional resources.

**Artifact / exit gate:** documented immutable plan keys, bounded cache ownership, credential-rotation behavior,
and cold/warm tests showing no structural recompilation or adapter election on the warm path.

### Step 7 — Implement the mandatory surface for the selected conformance kind

Choose the kind explicitly. It describes observable behavior independently of the C# inheritance hierarchy:

- **Source Integration** reaches a named source and earns one or more granular Inspection, Record Results, or Named
  Reads without implementing an Entity repository.
- **Entity Persistence** additionally implements the Data repository contracts
  `IDataRepository<TEntity,TKey>` and `IQueryRepository<TEntity,TKey>`.

Both kinds pass Source Core: exact activation/election, immutable source policy, resource ownership, cancellation,
stable failure semantics, diagnostics/redaction, and honest capability rejection. Entity Persistence additionally
requires:

- non-mutating, idempotent, concurrency-safe reachability/readiness checks;
- policy-gated, idempotent storage provisioning only when the source grants storage-lifecycle authority;
- `Get` with stable identity conversion;
- `GetMany` with one output slot per input identity, in input order, using `null` for a missing or invisible record;
- upsert and delete with correct mutation outcomes;
- `UpsertMany`, `DeleteMany`, `DeleteAll`, and `RemoveAll(RemoveStrategy)` with scoped semantics, cancellation, and an
  honest count and atomicity claim;
- `CreateBatch`, including the deferred load-and-mutate semantics of `Update(id, mutate)` and honest behavior for
  missing identities, conflicts, cancellation, and partial failure;
- query and count over the complete pushable definition supplied by Data;
- accurate execution receipts for handled filter, sort, paging, projection, and count work;
- consistent native error-code/type translation into Data's failure taxonomy, with restricted original evidence;
- no message-text classification, probe-failure-as-not-found, swallowed mutation failure, or conflation of provider
  timeout with caller cancellation; and
- Entity lifecycle callbacks remain Data-owned and active on permitted external/read-write operations.

An adapter never computes its own residual predicate or silently materializes the full source to imitate an
unsupported provider operation. It never reports paging handled after applying a page in application memory.

**Artifact / exit gate:** Source Core is green, and Entity Persistence cells B-01–B-08 are green when that kind is
claimed. A Source Integration connector reaches this gate through its source capabilities alone.

### Step 8 — Add external-source features by earned capability

These capabilities are orthogonal to CRUD. Inspection claims are granular; describe/resolve support does not depend
on listing or sampling support:

- storage-container listing, safe address resolution, description, and bounded sampling as separate cells;
- logical-property mapping to physical names, paths, and structured values, only with Entity Persistence;
- lossless buffered `RecordSet` materialization;
- registered reads with provider-native bindings;
- provider-enforced read-only connections or transactions where available.

Provider-specific Direct/raw APIs are expert escape hatches governed by their Direct contracts. They remain
inventoried and source-policy-gated outside the Source Integration capability.

Declare Source Integration support once in the pure `DescribeSource(source)` descriptor. Koan projects its
registered-read, record-result, and granular inspection flags into the same runtime claim set used by diagnostics and
certification; do not repeat those profiles in adapter-local claim code.

The Data-owned source plan gates effects before Entity lifecycle callbacks, storage readiness/provisioning, cache,
transaction creation, or provider I/O.
The adapter consumes the effective plan. Call-level switches, connection overrides, and instruction payloads cannot
elevate it.

**Artifact / exit gate:** each earned profile has its own green cell set; optional unclaimed paths have corrective
negative facts. No adapter-local option bypasses the source plan.

### Step 9 — Explain every decision

The same compiled plan should feed three projections:

```csharp
var source = Data.Source("LegacyErp");

var description = source.Describe();                 // pure: route, policy, capabilities, mappings
var explanation = source.Explain("orders.recent");  // pure: effect, binding, bounds, expected execution
var diagnosis = await source.Doctor(ct);              // active: connectivity and non-mutating validation
```

The output teaches the correction and distinguishes provider work from Data work:

```text
LegacyErp/orders.recent: rejected before provider I/O
Conformance kind: Source Integration
Data access policy: ReadOnly
Declared effect: Read
Effective effect: Unknown
Correction: use a provider-enforced read binding or a binding the sqlserver adapter can classify as Read.

LegacyErp/Customer query: ready
Provider: filter(Name.Full) + order(Id) + limit(101)
Data: project CustomerDto; no residual filter
Bounds: 100 records / 4 MiB / 2 s
```

Facts and logs redact credentials, parameters, business values, and high-cardinality identifiers. A failure names
the source, operation, missing capability or violated policy, and a concrete correction.

### Step 10 — Prove behavior against a real store

Connector certification uses Testcontainers or another repository-owned real-provider harness. Fakes test Data
orchestration only. Run the shared conformance profiles below, then add provider-specific boundary facts.

**Artifact / exit gate:** the evidence packet defined in §10.7. Every Observed row is PASS, Declined paths have
negative proof, every Target row is PASS before its claim is advertised, public claims match facts/docs, and
cold/warm baselines exist before conformance.

## 6. Source policy contract

Storage-lifecycle authority and data access are separate monotonic axes:

| Storage lifecycle | Data access | Valid meaning |
|---|---|---|
| `Managed` | `ReadWrite` | Ordinary Koan-owned store; provider policy may permit storage-lifecycle mutation |
| `Managed` | `ReadOnly` | Frozen/snapshot store; no data write or storage-lifecycle mutation |
| `External` | `ReadWrite` | Koan may read/write mapped data but never change physical shape |
| `External` | `ReadOnly` | Safe exploration and source-deterministic integration |

The default for an ordinary source is `Managed + ReadWrite`. External onboarding should make
`External + ReadOnly` the obvious recipe, without conflating the two settings.

Restrictions only narrow:

- a source definition is frozen after composition;
- Entity, map, operation, and request scopes may narrow but never elevate it;
- raw connection overrides cannot bypass it;
- `ReadOnly` blocks upsert, delete, batch, compare-and-set, direct execute, write transactions, and storage-lifecycle
  mutation;
- `External` blocks ensure-created, alter, index creation, schema clear/drop, operation provisioning, and implicit
  provider auto-create caused by connection/file open, first insert, first index, or another native convenience;
- an external route requires an existing target and a provider mode that cannot create it as a side effect;
- `External + ReadWrite` permits mapped data deletion, including semantic `DeleteAll`. Data may lower it to a clear
  primitive only after proving Entity lifecycle, segmentation, and non-structural equivalence;
- `Optimized` remove may downgrade to an honest non-structural data path. An explicitly requested structural `Fast` remove rejects
  when storage-lifecycle authority is absent; and
- `ReadOnly` admits only proven `Read`; `External` admits only proven `Read` or data `Write`. Effective `Unknown`
  fails whenever either ceiling is active.

Non-mutating inspection, reachability, and declared-shape validation remain valid in all four cells, but may not
open a provider in a mode that creates a file, database, collection, index, directory, or other target.

The framework guard is a semantic guarantee and fast corrective failure. Provider-enforced read credentials,
sessions, transactions, or invocation modes remain the hard security boundary, especially for functions, scripts,
and commands whose effects are opaque.

### Readiness and storage-lifecycle state model

Readiness and provisioning are distinct stages selected by the compiled source plan:

| Stage | Provider I/O | May mutate storage | Allowed posture | Completion rule |
|---|---|---|---|---|
| Describe/Explain | no | no | all | pure projection of frozen plans |
| Reachability | optional | no, including implicit open/create | all | route can be contacted in a non-creating mode |
| Declared-shape validation | yes | no | all | exact target/shape facts observed or corrective failure |
| Provision/repair | yes | yes | Managed + ReadWrite with explicit policy | requested additive work completes and validates |
| Business dispatch | yes | only its proven data effect | effective source ceiling permits it | result/receipt or stable failure outcome |

Route readiness and container/shape provisioning use separate host-owned single-flight keys. One caller may cancel
its wait without cancelling shared work; host shutdown owns the shared cancellation token. Failure/cancellation is
not cached healthy. A business operation is never the shape probe and is never replayed after a missing-shape guess.

### Failure, outcome, and replay matrix

| Boundary | Required outcome | Automatic replay |
|---|---|---|
| Before native dispatch | stable classified failure; acquisition may retry only when transient and bounded | permitted before dispatch |
| Read dispatched, response absent/partial | failure unless the provider proves a bounded complete/limited result | none for buffered registered reads |
| Write fails before commit point | bounded rollback with independent cleanup token; non-commit known | only when a separate idempotency contract proves safety |
| Cancellation/transport loss across commit | explicit outcome-unknown failure; retain provider and rollback evidence | never |
| Provider reports committed | success only after required receipt/result conversion completes or a typed committed-but-result-failed outcome exists | never |

Timeout is not caller cancellation. Retryability is not replay safety. Message text, instruction prefixes, or
“probably missing” classification never decide effect, commit outcome, target existence, or provisioning.

## 7. Hot-path contract

A golden-reference adapter has a measurable cold path and a structurally minimal warm path.

The **cold path** is the first legitimate use of a route, Entity shape, mapping version, or registered operation after
composition or structural invalidation. The **warm path** starts after its immutable plan and readiness result are
available. Credential rotation may refresh a physical client or pool; it must not force structural mapping or
query-plan compilation when the route shape is unchanged.

### Warm-path invariants

- no shape discovery, storage-lifecycle decision, reflection, catalog mutation, or capability negotiation;
- no provider I/O or contended lock merely to confirm an already-healthy readiness result;
- no synchronous wait over async I/O and no `ContinueWith` bridge;
- no JSON serialization round-trip to materialize flat records or DTOs;
- no repeated parse/build of the same physical structured value for each property mapped inside it;
- no repeated reflection or equivalent plan work for the same operation parameter shape;
- no per-record reconstruction of equivalent `RecordSet` field-name lookup metadata;
- no business operation used as a missing-shape probe followed by provision-and-replay; cold readiness validates or
  provisions before dispatch, and warm execution never replays an ambiguously failed operation;
- no `StartsWith("select")` or equivalent text heuristic for effects;
- no per-record connection creation inside a bulk operation;
- no unbounded materialization hidden behind streaming or paging grammar;
- no unbounded route, mapping, native-operation, or operation-plan cache;
- no mutable process-static state that crosses Koan hosts; and
- cancellation reaches the provider and disposes the active request, result/cursor/stream, and transactional resources.

### Golden-reference simplicity gate

Each semantic concern has one owner. The architecture review rejects adapter-local orchestration, duplicate policy
gates, duplicate route/readiness/mapping caches, and provider-specific copies of common materializers. The adapter
publishes a one-page responsibility map showing what Data, the family substrate, and the provider package each own;
P-06 makes that placement objectively reviewable. Provider complexity stays local while framework semantics stay
with their shared owner. Use the canonical [Data Adapter Responsibility Map](data-adapter-responsibility-map.md) as
the placement checklist; an adapter may specialize its native column without changing the other owners.

### Benchmark cells

Capture allocation, provider dispatch count, elapsed time, and provider work for each applicable cell:

| Cell | Applies to |
|---|---|
| cold composition and pure route description | Source Core |
| first legitimate readiness/shape use and warm no-op readiness | Source Core |
| read-only write rejection before provider I/O | Source Core |
| concurrent pool saturation and disposal | Source Core |
| bounded regular/nested `RecordSet` and DTO projection | Record Results |
| named record query and exactly-one scalar | Registered Reads |
| warm keyed get, bounded filtered page, and single upsert | Entity Persistence |
| 1,000-record native bulk in the claimed atomicity mode | Entity bulk/Atomic Batch |
| compiled flat/object/hybrid/path materialization | Relational Mapping |
| representative single/composite filter and order | Physical Projection and Indexing |
| early cancellation of a provider-bounded stream | Provider-Bounded Paging |

Each adapter records provider-native baselines for its applicable cells. A change fails the performance gate when it
adds structural work to a warm operation or materially regresses the pinned baseline without a documented waiver and
reproducible evidence. Native bulk cells set a dispatch-count ceiling; materialization cells set an allocation budget;
indexed cells capture the native plan and reject a scan or temporary sort where an applicable-index claim exists.
Thresholds live with executable benchmarks after a stable baseline exists.

## 8. Conformance profiles

An adapter earns small, explicit claims rather than universal parity. **Source Core** is mandatory for every connector
in this primer. **Entity Persistence** applies only when ordinary Entity APIs are exposed. The remaining rows are
earned capabilities or policy postures. A compound feature is conformant only when every listed dependency passes.
For Observed scope, a predicate evaluates the pinned surface or public claim. For Target scope, it evaluates the
explicit Target manifest as if that surface existed. “Announced” below refers to the surface in the evaluated scope; the
Advertised/Unadvertised publication axis remains independent.

| Profile or capability claim | Deterministic Observed/Target applicability | Acceptance cells |
|---|---|---|
| Source Core | every connector | A-01–A-06, C-04, C-06, G-02–G-04, G-08, H-01–H-06, P-01, P-03, P-05, P-06 |
| Entity Persistence | ordinary Entity surface is exposed | B-01–B-08, P-02, P-04 |
| Declared-shape validation | any mapping, projection, index, or other expected physical shape is declared | A-07 |
| Managed storage lifecycle | connector may provision or repair physical shape | A-07, A-09, G-01 |
| Read-only source safety | connector exposes any mutating surface | C-01, C-04, C-06 |
| External lifecycle safety | connector can create/alter/drop or may implicitly create storage | A-08, C-02, C-04, C-06 |
| External data-write safety | Entity Persistence exposes semantic data writes under `External + ReadWrite` | C-03, C-04, C-06 |
| Container listing | provider honestly enumerates neutral containers | D-01 |
| Container address resolution | provider resolves a neutral address | D-02 |
| Container description | provider reports neutral traits/shape | D-03 |
| Record sampling | provider samples a declared record-producing container | D-04 plus Record Results |
| Record Results | neutral buffered records are returned by any surface | D-05–D-08, P-02 |
| Identity-plus-object mapping | one identity value and one complete structured value are claimed | A-07, E-01, E-05–E-07, E-09–E-11, P-02 |
| Flat-name mapping | scalar physical names, including nested logical paths, are claimed | A-07, E-02, E-05–E-07, E-09–E-11, P-02 |
| Hybrid mapping | scalar values and a structured subtree are claimed together | A-07, E-03, E-05–E-07, E-09–E-11, P-02 |
| Scalar nested-path mapping | a flat property binds inside a physical structured value | A-07, E-04, E-05–E-07, E-09–E-11, P-02 |
| Selective read projection | provider column/value pruning is announced | E-08, P-04 |
| Physical projection and indexing | projection/index metadata is announced | A-07, E-12–E-13, P-04 |
| Rewrite-free derived/expression index | existing records are announced to benefit without rewrite | E-14 |
| Native TTL | TTL metadata is announced | E-15 |
| Registered Reads | `Query` or `Scalar<T>` is exposed | C-05, F-01–F-12; record queries also require Record Results |
| Provider-bounded paging | provider-bounded page/stream token is announced | B-09, P-04 |
| Atomic batch | an all-or-nothing batch token is announced | G-05 |
| Conditional replace | native compare-and-set token is announced | G-06 |
| Durability | connector announces durable persistence | G-07 |
| Isolation | each announced row/container/database isolation token | G-09, one case per token |
| Provider-native inspection | richer native metadata extension is exposed | D-09 |
| Vector Core | every connector exposing `Vector<TEntity>` | Source Core plus V-01–V-11, V-20, V-23–V-24 |
| Eventual Vector Visibility | source explicitly selects `Eventual` | V-12 |
| Vector Filters | metadata `Where` is announced | V-13 |
| Vector Hybrid | `Text` search is announced | V-14 |
| Named Vector Spaces | more than one vector space per point is announced | V-15 |
| Vector Continuation | resumable search is announced | V-16 |
| Vector Bulk | native bulk save or delete is announced | V-17 |
| Vector Atomic Batch | all-or-nothing batch is announced | V-18 plus G-05 |
| Vector Export | bounded export is announced | V-19 |
| Managed Vector Lifecycle | create or repair is allowed | V-20 plus A-07, A-09, G-01 |
| Vector Isolation | each announced row/container/database mode | V-21 plus G-09, one case per mode |
| Entity/Vector Coordination | `SaveWithVector` is exposed | V-22 |

This table is the applicability rule. Each Observed surface or public claim and each Target claim matches one
predicate above. Source Core's bounded soak is the common resource-hygiene gate; earned profiles add their own
representative operations to that same soak. A golden-reference adapter publishes its exact manifest and passes
every applicable cell; provider-specific claims and exclusions belong in its evidence packet.

## 9. Normative requirement catalog and acceptance scenarios

The stable IDs below are the one audit/conformance catalog. Earlier sections explain intent; build artifacts,
scorecards, tests, findings, and remediation records reference these IDs. The bracketed codes are all required evidence
kinds from §10.4 for each applicable case. Provider-specific tests supplement them; they never weaken them.

An ID may expand into cases but never hide them. “Every,” “each,” an enumerated surface, a declared operator, or a
capability token creates one scorecard row per discovered case. Mixed ownership creates separate linked rows. The
row key is `<Acceptance ID>/<Case>/<Owner>`; the stable acceptance ID itself does not change. Every applicable
case must pass.

### A. Reference, routing, and storage lifecycle

- **A-01** [STATIC, BOOT] Referencing the connector makes it available through ordinary `AddKoan()` with no provider
  registration call.
- **A-02** [BOOT, NEG] An explicit provider/source resolves exactly or fails with available choices and a correction.
- **A-03** [BOOT, NEG] An available but unelected connector performs no connection, file, shape, or readiness I/O.
- **A-04** [LIFE] Two Koan hosts do not share mutable plans, readiness state, keepers, or logical client state.
- **A-05** [LIFE] Host disposal releases every host-owned provider resource.
- **A-06** [BOOT] An ordinary source with no override resolves to `StorageLifecycle: Managed` and `Access: ReadWrite`.
- **A-07** [LIVE, NEG] Every physical shape the connector claims to validate is compared by definition, not name.
  An incompatible same-named object is unhealthy; repair occurs only under explicit managed lifecycle policy.
- **A-08** [LIVE, NEG, PLAN] External lifecycle policy performs no repair mutation or implicit provider auto-create.
- **A-09** [LIVE, FAULT, PLAN] Explicitly authorized provision/repair creates or reconciles the declared shape,
  post-validates it, and is idempotent; a repeated healthy call performs no mutation.

### B. Entity correctness

- **B-01** [LIVE, ORACLE] Every supported identity and value boundary round-trips without culture- or JSON-induced drift.
- **B-02** [LIVE, ORACLE] Get-many returns one slot per requested identity in input order, with `null` for each missing
  or invisible record.
- **B-03** [LIVE, ORACLE, FAULT] Upsert and delete report correct outcomes for insert, update, missing, conflict, and
  provider-failure cases.
- **B-04** [LIVE, ORACLE, NEG] Each exposed delete-all, remove strategy, native bulk path, and batch operation preserves
  active guards, transforms, Entity lifecycle callbacks, and isolation. Each path is a separate case; optimization
  never widens scope or changes announced atomicity.
- **B-05** [LIVE, FAULT] Deferred batch mutation loads at commit time, has a specified missing-record outcome, and
  participates in the same conflict and atomicity contract as other queued work.
- **B-06** [LIVE, ORACLE] Query results match the shared CLR oracle for each declared filter operator.
- **B-07** [LIVE, ORACLE, PLAN] Sort, page, projection, and count receipts match work the provider actually performed;
  each handled component is a separate case.
- **B-08** [NEG, PLAN] Each unsupported Entity operation rejects before unbounded work or partial mutation.
- **B-09** [LIVE, ORACLE, PLAN, FAULT] A provider-bounded page/stream applies the requested bound and complete order
  natively, reports an accurate receipt, propagates cancellation, and releases resources on early disposal.

### C. Source safety

- **C-01** [NEG, PLAN] Each write surface fails before Entity callbacks, readiness, transaction creation, or provider I/O
  for that operation on a read-only source.
- **C-02** [NEG, PLAN] Each storage-lifecycle/provisioning surface fails at the same boundary on an external source.
- **C-03** [LIVE, ORACLE, PLAN, NEG] External/read-write semantic `DeleteAll` remains legal. A clear primitive is legal
  only after Data proves equivalence; `Optimized` may downgrade, while explicitly structural `Fast` rejects.
- **C-04** [NEG, PLAN] Nested context, mapping, Direct/connection override, transaction, batch, instruction, and provider
  extension paths cannot elevate either source restriction; each exposed path is a case.
- **C-05** [NEG, PLAN] Every registered read has effective effect `Read`; `Unknown` rejects before execution.
- **C-06** [STATIC, NEG] Facts and exceptions report effective policy without exposing the connection string.

### D. Inspection and records

- **D-01** [LIVE, ORACLE] Listing is bounded, distinguishes `Complete`, `MoreAvailable`, and `ProviderLimit`,
  and returns a source-bound opaque continuation exactly when the provider can resume.
- **D-02** [LIVE, NEG] Resolution uses provider-safe identifier handling, returns a source-bound opaque reference,
  reports typed ambiguity and safe candidates, and rejects a reference used with the wrong source.
- **D-03** [LIVE, ORACLE] Description is non-mutating and reports honest provider kind, intrinsic traits,
  source-policy-projected effective operations, optional neutral shape, and diagnostic-only display path.
- **D-04** [LIVE, ORACLE, NEG] Sampling is available only for intrinsic `Records` and effective `Sample`, is bounded,
  uses the regular shared shape or one nested document value, reports completion, and never mutates.
- **D-05** [LIVE, ORACLE] Field order, duplicate names, provider type, missing, null, binary, temporal, numeric, JSON,
  and nested values survive neutral materialization.
- **D-06** [LIVE, NEG] Ambiguous name lookup rejects, ordinal lookup remains lossless, and an additional result channel
  rejects rather than being discarded.
- **D-07** [ORACLE, PERF] DTO projection uses a reused ordinal plan for immutable constructor/positional-record and
  writable-property binding, with corrective missing/duplicate/type failures and no dictionary-to-JSON conversion.
- **D-08** [LIVE, ORACLE, NEG] Every buffered result has positive record/value/aggregate/duration limits, uses the shared
  byte-accounting basis, omits rather than clips the first non-fitting record, and reports the first honest completion
  reason; cancellation, timeout, conversion, and provider failure remain failures.
- **D-09** [STATIC, LIVE, NEG] Provider-native inspection preserves richer metadata in an explicit provider type and
  neither widens nor mislabels the neutral descriptor or `RecordSet` contract.

### E. Mapping

#### Mapping shapes and common rules — E-01 through E-11

- **E-01** [LIVE, ORACLE] An identity-plus-object mapping round-trips.
- **E-02** [LIVE, ORACLE] Flat scalar names, including nested logical property paths, round-trip.
- **E-03** [LIVE, ORACLE] Hybrid authoritative scalar bindings and structured subtrees round-trip without duplication drift.
- **E-04** [LIVE, ORACLE] A flat logical scalar mapped inside a physical structured value round-trips in both directions.
- **E-05** [BOOT, NEG] Logical/physical duplicates, ambiguous paths, missing required bindings, incompatible types, and
  undeclared asymmetry fail plan compilation with a correction; explicit generated/read-only bindings remain legal.
- **E-06** [LIVE, ORACLE, NEG] Single, generated, and composite identities use every declared component consistently;
  a partial or nullable composite identity rejects before execution.
- **E-07** [LIVE, ORACLE] Each declared legacy codec is symmetric, and query/write parameters use the same physical
  encoding as hydration.
- **E-08** [LIVE, PLAN] Read projection fetches only physical values required by the compiled map when that optimization
  is announced.
- **E-09** [LIVE, NEG, PLAN] External read/write mapping never causes DDL.
- **E-10** [STATIC, PERF] Mapping plans are immutable, host-scoped, bounded, and reused on the warm path.
- **E-11** [LIVE, ORACLE, PLAN] Every exposed consumer—hydration, writes, filters, ordering, patches, conditional
  writes, projections, and index expressions—uses the same compiled physical binding for each logical path. Each
  exposed consumer is a case; index-expression cases exist only with that separate profile.

#### Physical projection and indexing — E-12 through E-14

- **E-12** [LIVE, ORACLE, PLAN] A stored projection used by a query or index is maintained by every mutation case;
  otherwise the index derives from the canonical stored value. Fallback cannot conceal an inert index or false claim.
- **E-13** [LIVE, PLAN] A declared usable index covers the same provider expression and scalar encoding emitted for its
  filter/order. Native plan evidence proves representative single and composite cases.
- **E-14** [LIVE, PLAN] An announced rewrite-free derived/expression index benefits records already stored without
  rewrite. Required backfill is an explicit deployment operation and that capability remains unannounced.

#### Native TTL — E-15

- **E-15** [LIVE, NEG, PLAN] TTL metadata is lowered only when native expiry semantics are announced and proved; it
  never becomes a meaningless ordinary index.

### F. Registered operations

- **F-01** [BOOT, NEG] Duplicate `(source, name)` declarations fail composition; the catalog cannot mutate afterward.
- **F-02** [NEG, PLAN] `Query` resolves only `Read + Records + Buffered` and `Scalar<T>` only
  `Read + Scalar + Buffered`; result, delivery, or effect mismatch fails before provider execution.
- **F-03** [NEG, PLAN] Missing, extra, null-for-required, and incompatible parameters fail before provider execution.
- **F-04** [STATIC, PERF] The adapter receives the selected binding, frozen execution lane, reused parameter plan,
  effective-read decision, timeout, and bounds without reparsing application configuration.
- **F-05** [BOOT, LIVE, NEG] Detectable mutation under a read declaration fails composition. Every opaque read is bound
  permanently to provider-enforced read-only execution; inability to establish it fails composition/validation.
- **F-06** [NEG, PLAN] `Unknown` never runs through `Query` or `Scalar` on any source.
- **F-07** [LIVE, ORACLE, NEG] A record result satisfies Record Results. A scalar is exactly one provider scalar or one
  record × one field, respects the shared value/duration bounds, and rejects every other cardinality.
- **F-08** [LIVE, NEG, PLAN] External registration may carry inline payload but never provisions a provider-managed artifact.
- **F-09** [LIVE, NEG] A missing referenced provider-managed artifact fails diagnostically and is not created externally.
- **F-10** [STATIC, NEG] Registered operations are uncached, are not automatically exposed through REST/MCP, and fail closed
  under active segmentation unless an explicit host/control-plane scope exists.
- **F-11** [FAULT, PLAN] No registered operation is replayed after provider dispatch. Bounded transient acquisition retry is
  pre-dispatch; provider-transparent retry must be announced and proved as its own case.
- **F-12** [STATIC, LIVE] Telemetry records source, stable name, provider, effect, result kind, duration, attempts, and
  result count—not payload text or parameter values.

### G. Concurrency, faults, durability, and isolation

- **G-01** [LIVE, FAULT] Concurrent first legitimate shape use has one readiness/provisioning outcome and no partial race.
- **G-02** [FAULT, LIFE] Route readiness and shape provisioning use distinct host-scoped single-flight state. Caller
  cancellation detaches one wait; shutdown owns shared cancellation; failure is never cached healthy.
- **G-03** [LIVE, FAULT, LIFE] Pool saturation does not leak source, transaction, read-only, tenant, or provider session
  state to the next operation.
- **G-04** [FAULT, LIFE] Caller cancellation and provider timeout remain distinct on each native operation; cancellation
  reaches provider dispatch and releases active request, result/cursor/stream, and transactional resources.
- **G-05** [LIVE, FAULT] A claimed atomic batch leaves state wholly before/after. Pre-commit failure rolls back with a
  bounded independent cleanup token; an indeterminate commit reports outcome unknown and is never replayed.
- **G-06** [LIVE, ORACLE, FAULT] Conditional replace is one native compare-and-set and reports a lost race without
  overwriting the winner.
- **G-07** [LIVE, LIFE] Restart proves announced durability; an in-memory shim cannot pass.
- **G-08** [LIFE, PERF] The standard bounded soak leaves client/pool, handle, result/cursor/stream, task, cache, and
  memory counts stable.
- **G-09** [LIVE, ORACLE, FAULT] Each announced isolation token passes the shared adversarial cross-scope cell.

### H. Diagnostics and privacy

- **H-01** [STATIC, BOOT] `Describe` is side-effect-free and matches compiled route, policy, mappings, and capabilities.
- **H-02** [STATIC, BOOT] `Explain` reports provider work, client work, bounds, and unsupported semantics before execution.
- **H-03** [LIVE, NEG] `Doctor` performs only documented non-mutating checks and gives a correction for every failure.
- **H-04** [STATIC, BOOT] Startup, facts, health, errors, and tests project the same decision identities.
- **H-05** [STATIC, NEG] General facts/logs exclude credentials, parameters, business values, tenant identifiers, and
  full raw provider errors; safe public exceptions and restricted native evidence stay separate.
- **H-06** [LIVE, FAULT, NEG] Native failures use exact provider code/type and target, never message text. Timeout is not
  caller cancellation; `OperationCanceledException` survives only when non-commit is known. Retryability and replay
  safety remain separate, and a failed mutation/existence probe never becomes success/not-found.

### V. Vector persistence, search, and lifecycle

- **V-01** [STATIC, BOOT, NEG] A vector-space plan binds source, safe physical name, dimensions, metric, visibility,
  optional model identity, and optional named space once; an unelected provider performs no I/O.
- **V-02** [LIVE, ORACLE, NEG] Empty, non-finite, wrong-dimension, or wrong-space embeddings reject before mutation;
  every valid boundary value round-trips without drift.
- **V-03** [LIVE, ORACLE] Saving a new identity inserts one point; saving it again atomically replaces embedding and
  metadata without a duplicate.
- **V-04** [LIVE, ORACLE, FAULT] Delete returns the correct existing/missing outcome; provider failure never reports
  success and does not widen scope.
- **V-05** [LIVE, ORACLE] Get-one returns one complete point or `null`; get-many preserves input count, order, and
  duplicates, with `null` in every missing slot.
- **V-06** [LIVE, ORACLE, NEG] Metadata survives the neutral value algebra without reserved-field collision, shape
  invention, provider objects, or missing/null confusion.
- **V-07** [LIVE, ORACLE] Search returns at most `Top`, no duplicate identity, descending similarity, and stable identity
  tie order; empty and fewer-than-requested results are normal.
- **V-08** [LIVE, ORACLE, PLAN] Similarity is finite `[0,1]`, higher is closer, monotonic for the declared metric, and
  `AtLeast` is applied equivalently or rejected before unbounded work.
- **V-09** [LIVE, ORACLE, NEG] Search uses exactly the declared source, dimensions, metric, model, and named space; a
  point or query from another space never mixes silently.
- **V-10** [LIVE, ORACLE, PLAN] Exact/Approximate and candidate facts report provider work honestly; approximation is
  never presented as an exact oracle result.
- **V-11** [LIVE, ORACLE, FAULT] Default Session visibility makes each awaited save/delete observable to subsequent get
  and search operations in the same source without arbitrary sleeps.
- **V-12** [LIVE, ORACLE, FAULT] Explicit Eventual visibility may defer search visibility; `Sync` is cancellable,
  bounded, and makes every earlier accepted mutation in that source visible or fails.
- **V-13** [LIVE, ORACLE, NEG, PLAN] `Where` constrains the candidate set before ranking. The shared filter oracle agrees
  for every announced operator; residual or post-filter fallback rejects before provider I/O or unbounded work.
- **V-14** [LIVE, ORACLE, NEG, PLAN] Hybrid search defines `SemanticWeight` endpoints as pure lexical/vector modes,
  preserves normalized final rank, and rejects unsupported text/weight combinations before I/O.
- **V-15** [LIVE, ORACLE, NEG] Each named space keeps immutable dimensions, metric, and model; unsupported or ambiguous
  selection fails with safe available choices.
- **V-16** [LIVE, ORACLE, NEG] A continuation is opaque and bound to source, space, query, filter, and ordering; resume
  has no duplicates or gaps inside its declared snapshot contract, and wrong-context reuse rejects.
- **V-17** [LIVE, ORACLE, FAULT, PLAN] Native bulk save/delete preserves per-item outcomes, cancellation, guards,
  visibility, and isolation; dispatch count and partial failure are reported honestly.
- **V-18** [LIVE, ORACLE, FAULT] An Atomic Batch claim is all-or-nothing under injected mid-batch failure; otherwise the
  result is explicitly non-atomic and retains every item outcome.
- **V-19** [LIVE, ORACLE, FAULT] Export is provider-bounded, cancellable, and yields every visible point exactly once
  under its stated snapshot or weak-consistency contract without materializing the corpus.
- **V-20** [LIVE, NEG, PLAN] Ensure, validate, and clear obey lifecycle and access policy, compare the complete space
  shape, never auto-create under External, and keep visibility synchronization distinct from destructive clearing.
- **V-21** [LIVE, ORACLE, NEG] Every announced vector isolation mode prevents cross-scope get, search, export, and delete
  and maps to the declared metadata or physical-address mechanism; a pinned conflicting name rejects at plan time.
- **V-22** [LIVE, FAULT, NEG] Entity/vector coordination never invents cross-store atomicity; every injected stage
  failure exposes safe commit facts, retry disposition, and compensation guidance without provider secrets.
- **V-23** [FAULT, NEG, LIFE] Cancellation, timeout, rate limit, schema mismatch, unavailable provider, and disposal map
  to the shared failure taxonomy; disposal releases clients, cursors, polling, and background work.
- **V-24** [PERF, PLAN] Warm single save/get/search, native bulk, filtered search, and materialization meet pinned
  provider-relative budgets with one compiled immutable plan and no per-result reflection or metadata-shape rebuild.

### P. Hot-path and golden-reference quality

- **P-01** [PERF] Warm source routing performs no DI enumeration, adapter election, policy recomputation, readiness I/O,
  or structural plan compilation.
- **P-02** [PERF] Warm Entity/mapping/record paths perform no repeated reflection, mapping conflict detection,
  dictionary-to-JSON materialization, equivalent lookup reconstruction, or repeated physical-JSON parsing.
- **P-03** [STATIC, LIFE, PERF] Route, readiness, mapping, native-operation, and operation caches are host-scoped, bounded, and
  invalidated by stable structural/credential rules.
- **P-04** [ORACLE, PLAN, PERF] No handled/optimized claim conceals a full-source materialization, client page, inert
  index, temporary sort, N+1 provider-dispatch loop, or other weaker execution.
- **P-05** [PERF] Every applicable §7 benchmark records allocation, provider dispatch count, elapsed time, and provider work against
  a pinned baseline; structural warm-path regression or unexplained material regression fails.
- **P-06** [STATIC] The one-page responsibility map has one owner for each policy, route, readiness, mapping, materializer,
  and provider-execution concern; duplicate adapter-local framework machinery fails placement review.

## 10. Evaluate an adapter and choose its change path

An evaluation records contract conformance. Begin with public behavior and claims, then inspect code to assign each
observed fact to Framework, a shared Family substrate, or the Adapter. The contract establishes architectural
authority; implementations supply evidence and lessons.

### 10.1 Pin the audit identity

Pin the audit identity before reading implementation or running tests:

| Adapter/package | Reproducible source identity | Primer revision/status | Provider and driver versions | Real-store fixture | Source policies exercised | Date |
|---|---|---|---|---|---|---|
|  |  |  |  |  | Managed/RW · Managed/RO · External/RW · External/RO |  |

A clean tree may use its commit as the source identity. Otherwise pin the base commit plus a content-addressed patch,
added-file manifest, and resultant source fingerprint that reproduce in a disposable clean worktree. A commit that
does not contain the code under test is not a valid identity.

Declare the conformance kind and separate Observed scope from Target scope:

| Claim ref | Kind/profile/token | Scope | Publication | Observed/Target applicability fact | Fail-closed evidence when declined | Notes |
|---|---|---|---|---|---|---|
| CLM-001 | Source Core | Observed | Unadvertised | every connector | — | mandatory |
| CLM-002 | Entity Persistence | Observed | Advertised | ordinary Entity APIs are public and trigger §8 | — |  |
| CLM-003 | Native TTL | Declined | Unadvertised | no Observed or Target token | EV-… | corrective unsupported result |

The two axes have one meaning:

- **Scope = Observed** — a surface or public claim in the pinned audit identity selects the §8 predicate. Every
  Advertised claim is Observed even when its implementation is absent.
- **Scope = Target** — the product/maintainer-approved work-item examples and Target manifest select the §8 predicate.
  Target is Unadvertised; an auditor cannot invent or remove Target scope to improve a verdict.
- **Scope = Declined** — no Observed or Target predicate selects the optional capability; it is Unadvertised and has
  corrective/fail-closed evidence.
- **Publication = Advertised/Unadvertised** — whether the pinned API, capability facts, or documentation announces it.

The pinned identity freezes publication. An Advertised capability is Observed/Advertised even when evidence fails;
a withdrawal or non-shipping disposition requires explicit product/maintainer authority and a separately pinned audit
identity. Observed and Target versions of one feature use separate `CLM-*` rows.

Capture provider research in a reproducible ledger:

| Probe ref | Concern | Provider/driver version | Least-privilege posture | Exact probe/fixture | Observation | Native artifact | Official source |
|---|---|---|---|---|---|---|---|
| PRB-001 |  |  |  |  |  |  |  |

No real-store fixture means the provider behavior is unverified. A mock, code path, comment, capability self-report,
or passing compile cannot upgrade that status to green.

### 10.2 Inventory every execution surface

Discover implementations and shared family layers dynamically from the repository. Inventory every public and
optional path before selecting tests:

| Surface | Paths that must be accounted for |
|---|---|
| Activation and participation | package/module discovery, exact election, startup initializer, health, readiness, disposal |
| Keyed Entity access | get, get-many, upsert, delete, conditional replace, polymorphic root |
| Query | filter, sort, page, count, projection, provider-bounded stream, execution receipt |
| Bulk and coordinated writes | upsert-many, delete-many, delete-all, each remove strategy, batch add/update/mutate/delete, transaction |
| Expert/alternate paths | Direct/raw, instructions, patch, storage-lifecycle commands, connection override, provider extensions |
| External source access | inspect/list/resolve/describe/sample, `RecordSet`, DTO projection, named query/scalar |
| Physical realization | mapping, codecs, storage naming, partition/axes, shape, projections, indexes, TTL |
| Cross-cutting failure paths | cancellation, timeout, conflict, transient fault, ambiguous commit, retry/replay, redaction |

Record the inventory in this table:

| Surface ref | Public entry/path | Claim ref | Source postures | Effect/result | Plan + semantic owner | Native owner/dispatch | Failure/cancellation path | Acceptance cases | Unsupported outcome |
|---|---|---|---|---|---|---|---|---|---|
| SUR-001 |  |  | Managed/RW · Managed/RO · External/RW · External/RO |  |  |  |  |  |  |

For each path, trace one direction:

```text
application intent
  → immutable source/effect/mapping plan
  → readiness or policy-gated provisioning
  → provider resource owner
  → native dispatch
  → execution receipt, result, or stable failure outcome
```

If a path skips a stage, the audit records why that is safe. “Another path checks it” is not evidence.

### 10.3 Map profiles to stable acceptance cells

§8 determines applicability. Expand every selected catalog ID using the §9 case rule and the SUR inventory. Split
mixed Framework, Family, and Adapter concerns into linked rows. A row is atomic: one acceptance ID, one case, one
owner.

The claim-to-cell matrix is the bridge:

| Claim ref | Observed/Target predicate | Acceptance IDs | Required cases from SUR/probe ledger | Scope | Publication | Declined-path evidence |
|---|---|---|---|---|---|---|
| CLM-001 | connector referenced | A-01–A-06, C-04, C-06, G-02–G-04, G-08, H-01–H-06, P-01, P-03, P-05, P-06 |  | Observed | Unadvertised | — |
| CLM-… |  |  |  |  |  |  |

The working scorecard is:

| Row ID | Acceptance ID | Case | Claim ref/scope/publication | Owner | Linked rows | Required evidence | Evidence refs | Verdict | Gap/failure | Remediation ref | Re-entry proof |
|---|---|---|---|---|---|---|---|---|---|---|---|
| F-01/catalog/framework | F-01 | immutable catalog | CLM-…/Observed/Advertised | Framework | — | BOOT, NEG |  |  |  |  |  |
| E-13/single-filter/adapter | E-13 | representative single-field filter | CLM-…/Observed/Advertised | Adapter | — | LIVE, PLAN |  |  |  |  |  |
| A-05/pool/adapter | A-05 | pooled client disposal | CLM-001/Observed/Unadvertised | Adapter | — | LIFE |  |  |  |  |  |

Where one case crosses owners, use row IDs such as `B-06/equals/framework` and `B-06/equals/adapter` and
link them bidirectionally. Required evidence is copied exactly from §9; an audit may add evidence but cannot delete a
required kind.

### 10.4 Require the right evidence

- `STATIC` — layer/API/capability inspection and narrow forbidden-pattern checks.
- `BOOT` — a consumer compiles and a real `AddKoan()` host activates and elects correctly.
- `ORACLE` — black-box result compared with the shared CLR/reference oracle.
- `LIVE` — integration fact against the pinned real provider; a fake cannot satisfy this evidence kind.
- `NEG` — policy rejection, unsupported/fail-closed, escape-path, and overclaim facts.
- `FAULT` — conflict, cancellation, timeout, network/storage fault, rollback, and indeterminate-commit injection.
- `PLAN` — provider-native plan, operation trace/count, and execution receipt.
- `LIFE` — two-host isolation, disposal, restart/durability, and bounded soak.
- `PERF` — cold/warm allocations, provider dispatch count, readiness cost, and elapsed-time baseline.

All codes attached to a catalog cell are conjunctive. Register evidence once and reference it from every row it
proves:

| Evidence ref | Kind | Exact test/command/review | Fixture/environment | Framework/driver/provider versions | Date | Artifact/log/plan | Result |
|---|---|---|---|---|---|---|---|
| EV-001 | LIVE |  |  |  |  |  |  |

An entry is reproducible only when another maintainer can run the exact command against the pinned fixture and find
the retained artifact. High-blast adapters add a narrow critical-seam review for process-static mutable state, policy bypasses,
sync-over-async, message-text failure classification, text-prefix effect inference, raw-error passthrough, and
unbounded caches. Black-box proof remains mandatory; this review adds defense in depth.

### 10.5 Apply mechanical verdict rules

- **PASS** — every required proof for the row ran green against the pinned fixture and audit identity; no silent
  fallback weakens it.
- **RED** — applicable Observed/Target implementation or evidence is absent without a named external blocker,
  executed evidence failed, an Advertised claim is false, a failure is swallowed, or a stronger claim hides fallback. Framework-owned
  RED is still RED.
- **DEFER** — the row is applicable but an exact missing framework prerequisite or unavailable external environment
  blocks execution. It records blocker, owner, safe posture, re-entry condition, and the claim that cannot ship.
  Ordinary pending work is RED; DEFER is not evidence.

Capability honesty follows directly:

- an Observed predicate absent from the manifest = RED;
- Observed/Advertised and failed = RED;
- any Observed row unrun = RED unless the DEFER rule's named blocker is present;
- optional, Unadvertised, and corrective fail-closed = Scope `Declined`, not a positive-cell verdict;
- implementation found without a declaration = declare and prove it, or retire/leave it unreachable; for a selected
  whole-adapter `REBUILD`, every legacy implementation route must be retired rather than left unreachable; and
- an unsupported feature that silently emulates, scans, flattens, mutates, or returns partial success = RED.

The pinned adapter is conformant only when every Observed row is PASS and every Declined path has negative evidence.
An Observed RED or DEFER blocks release of the pinned adapter and claim set. A Target RED or DEFER blocks only that
Target claim's advertisement; it does not change the pinned Observed claim set. An Advertised feature retains
Observed scope and publication throughout the audit snapshot.

### 10.6 Assign ownership before changing code

Assign **Framework** when the fact is provider-neutral: source policy, immutable plans, query/residual coordination,
record/mapping/named-read contracts, common error and commit outcomes, capability gates, or TestKit. Assign a shared
**Family** substrate when relational, document, or key/value mechanics repeat without provider meaning. Assign
**Adapter** when the fact requires native knowledge: connection mode, identifier grammar, native types/codecs,
native operations, transactions, cancellation wiring, error codes, metadata, or resource lifecycle.

If both are involved, create two linked rows: Framework computes an immutable decision; Adapter realizes it and
returns a receipt. Never close a Framework RED with an adapter-local workaround.

Choose one remediation disposition for every non-PASS row:

- `KEEP` — placement is correct; add or retain evidence;
- `HOIST` — move duplicated provider-neutral behavior into Data or a family substrate;
- `LOCALIZE` — retain provider dialect/SDK behavior in the adapter;
- `SPLIT` — Framework emits the plan, Adapter executes and reports;
- `RETIRE` — remove duplicate, misleading, swallowed, or unreachable machinery;
- `REBUILD` — replace an adapter-owned slice or complete adapter behind a designated contract seam; or
- `DECLINE` — abandon an Unadvertised Target and fail closed, or schedule withdrawal of an Advertised claim under a
  separately pinned audit identity. It cannot close an Observed/Advertised RED in the same pinned identity.

Record the decision separately from the finding:

| Remediation ref | Decision | Rows | Evidence that forced it | Destination seam | Code retained | Code removed/replaced | Acceptance proof |
|---|---|---|---|---|---|---|---|
| R-01 | KEEP / HOIST / LOCALIZE / SPLIT / RETIRE / REBUILD / DECLINE |  |  |  |  |  |  |

#### Whole-adapter greenfield replacement

When a work item selects whole-adapter `REBUILD`, the old implementation is evidence, not source material. Harvest may
record provider facts, externally observable behavior, public contract requirements, regression scenarios,
performance traps, and patterns not to repeat. It must not prescribe the new file/class graph, helper structure,
control flow, caches, or internal test-fixture design.

The authoring boundary is architectural and testable, not biographical. Begin with empty adapter implementation files.
Derive every runtime type, cache, resource owner, dispatch boundary, and abstraction from a ratified contract or a
measured hot-path need. Review the result for copied structure and redundant concepts, but do not substitute role
registries, access logs, or claims of cognitive isolation for behavioral and structural evidence.

For the replaced Adapter owner, this choice overrides per-slice `KEEP`, `HOIST`, and `LOCALIZE`: record one complete
`REBUILD`, retire every legacy implementation route, and set `Code retained` to `none`. A shared Framework/Family gap
has its own contract-derived change; no old Adapter body moves into that owner.

| Lesson ref | Provider/public fact or negative lesson | Reproducible proof | Black-box consequence | Author-visible |
|---|---|---|---|---|
| L-001 |  | PRB-… / EV-… |  | yes / no |

Before authoring begins, freeze the rewrite inputs and empty the replaced adapter implementation. Valid inputs are this
primer, ratified public contracts, shared Framework/Family contracts, provider facts and official documentation,
public compatibility decisions, negative lessons, and black-box scenarios. Do not copy, port, mechanically transform,
or structurally preserve the retired source. Package identity, public names, configuration keys, and provider
dependencies continue only when explicitly ratified and are reimplemented as contracts.

The final change atomically deletes the old implementation and installs the new one. It leaves one activation path,
one native execution path per declared operation, and no legacy/compatibility/shadow/fallback route. Certification
includes a retirement manifest, compile/registration/type inventory, source-lineage review, and dead-path absence
proof. A post-build comparison may discover a missed valid public behavior; that behavior becomes a requirement and
black-box test for the new implementation, never a reason to restore old code or create a bridge.

### 10.7 Produce the evidence packet

An adapter evaluation is complete only when it publishes:

1. the pinned identity header and `PRB-*` provider probe ledger;
2. the conformance kind plus `CLM-*` Observed/Target/Declined scope and Advertised/Unadvertised publication manifest;
3. the `SUR-*` execution-surface matrix covering all four source-policy postures and every alternate path;
4. the claim-to-cell matrix and fully expanded atomic scorecard;
5. the `EV-*` registry and retained plans, provider dispatch counts, fault artifacts, lifecycle results, and P-cell baselines;
6. the verdict dependency index: consumed semantic owners, source path/hashes, conformance tool/schema and profile
   fingerprints, and provider fixture identity;
7. the `R-*` remediation ledger with linked owners, invalidated consumers, and re-entry proofs;
8. for whole-adapter `REBUILD`, the frozen rewrite inputs, empty-root proof, legacy-retirement, new-source, absence,
   architecture, and independent behavioral evidence; and
9. README, capabilities, facts, diagnostics, and known limits reconciled to the same pinned truth.

Run order is fixed: **pin → inventory → execute Source Core → execute every Observed cell → execute Target work
cells → prove Declined paths fail closed → assign owners → select remediation dispositions → invalidate every packet
that consumed a changed owner/path/tool/profile/fixture → rerun all affected rows on one new identity**.
The packet is reproducible only when every scorecard evidence reference resolves to its registry entry/artifact and
every public claim resolves back to `CLM-*` and green rows. Invalidation follows declared consumption, not the order of
work items: an earlier gold or sibling packet is stale when it consumed a changed dependency.

## Related

- [Compact provider-neutral Data adapter language](../decisions/DATA-0110-compact-data-adapter-language.md) — public
  mapping and registered-operation grammar
- [Koan Data reference](../reference/data/index.md) — Entity and adapter surface
- [Product surface](../reference/product-surface.md) — supported packages and evidence
- [Adding a connector](../engineering/adding-a-connector.md) — packaging and integration workbook
- [Koan Product Constitution](product-constitution.md) — application-language and capability-honesty rules
- [Entity access and streaming](../guides/data/entity-access-and-streaming.md) — bounded-stream contract
- [Provider-bounded Entity streams](../decisions/DATA-0107-provider-bounded-entity-streams.md) — paging capability
