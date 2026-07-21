---
type: ARCHITECTURE
domain: data
title: "The managed-field seam — generic storage mechanism for ambient axes (tenant, classification)"
audience: [architects, ai-agents]
status: proposed
last_updated: 2026-06-22
re_derives:
  - "DATA-0105 §2/§8 — the 'sibling column' write premise (errata filed; see §8)"
  - "tenancy-design.md §12 — 'a column alongside id' (errata filed; see §8)"
review: "adversarial 4-lens review (wf_d547d3d1-0fe) — ship-after-blocking-fixes; all 6 blockers folded below"
---

# The managed-field seam

> The generic, tenancy-agnostic storage mechanism by which a **module** (Koan.Tenancy, later classification)
> contributes an **invisible framework-managed field** — a value persisted with every record, scoped from an
> ambient axis, used to isolate reads/writes — **without the data core or any adapter knowing the axis.** This
> is the load-bearing seam for the tenancy capability (ARCH-0095 §5) and the first concrete realization of the
> DATA-0105 §0 contributor pattern. It refines DATA-0105's phase-3/4 mechanism after an empirical
> re-derivation, and was hardened by an adversarial review that found the original "the facade is the
> universal gateway" premise to be **incomplete** (§3).

## 1. The empirical correction (why this is not a sibling column)

The pivot premise was: *"relational stores `(Id, Json)` → the tenant discriminator is a natural SIBLING
COLUMN, no serialization hook needed."* **Re-derived against current source, that premise is false:**

| Fact | Evidence |
|---|---|
| Relational adapters persist **only `(Id, Json)`** on write; projected columns are **never populated**. | `SqliteRepository.cs:822` (`INSERT … (Id, Json) … ON CONFLICT(Id) DO UPDATE SET Json = excluded.Json`); PG/SqlServer `ToRow` emit `(Id, Json)` only. |
| Reads of a projected field fall back to **JSON access**; the physical projected columns are vestigial (always-NULL + COALESCE fallback). | `SqliteRepository.cs:623-638` (`COALESCE([col], json_extract(Json,'$.prop'))`). |
| A sibling column would need **new per-adapter write machinery**, and the discriminator (not a POCO property) is in neither the entity nor the JSON without a hook. | — |
| `FieldPathResolver.Resolve` **throws** on any non-POCO field; both `SqlFilterTranslator.VisitField` **and** the relational `FilterSplitter` call it (so one change fixes translation AND pushability). | `FieldPathResolver.cs:60-61`, `SqlFilterTranslator.cs:66`, `FilterSplitter.cs:56`. |
| The relational trio shares one serialization config point (`ComparableScalarEncoding.Apply`); but write injection is otherwise **per serialization stack** (Mongo=native BSON, JSON-file/Redis=own Newtonsoft). | SQLite:415, PG:290, SqlServer:92; `MongoRepository.cs:452`. |

**Conclusion.** The faithful realization of the ratified intent — *an invisible, framework-managed,
secure-by-default field that is not a POCO property* — is a **managed field injected into the persisted
record** (the Json envelope for relational; a BSON element for Mongo; a sibling key for JSON-file),
**filtered via managed-aware field-resolution** (one change to the shared `FieldPathResolver` reaches both the
relational translator and the pushability splitter). A real indexed column is an *optional* schema-stage
optimization (a computed/expression index over the JSON-access expression). This honors the architect's
"invisible shadow field" decision while correcting the storage mechanism the empirical reality demands. The
evidence table above is canon — the choice is not to be re-litigated.

## 2. The three generic seams (data core, tenancy-agnostic)

All three are **empty no-ops when no module registers** (`ManagedFieldRegistry.IsEmpty` ⇒ every path
short-circuits to byte-identical pre-change behavior). A module registers **descriptors** (data, not
behavior); the one declared per-axis operand is a `ValueProvider` delegate that reads the ambient axis once,
at the chokepoint. Descriptors are the DATA-0105 §0 contributor pattern realized for non-POCO ambient fields
(see §8 for the reconciliation with `IWriteStamp`/`[KoanDiscoverable]`).

### Seam 1 — `ManagedFieldRegistry` + `ManagedFieldDescriptor` (`Koan.Data.Abstractions`)

```
public sealed record ManagedFieldDescriptor(
    string StorageName,                 // literal persisted key, MUST be a fixed point of CamelCaseNamingStrategy
                                        //   (lead with '_' or be all-lowercase) — see §7; validated at Register
    Type ClrType,                       // string for tenant
    Func<object?> ValueProvider,        // reads the ambient axis ONCE at the chokepoint (e.g. () => Tenant.Current?.Id)
    Func<Type, bool> AppliesTo,         // e.g. t => !IsHostScoped(t)
    string? RequiredCapability = null,  // adapter must announce it (ARCH-0084) AND be IQueryRepository AND push
                                        //   scalar Eq on the managed field, else the op fails closed at boot
    bool Indexed = false);              // promote to a computed/expression index where supported (schema seam, §3c)

public static class ManagedFieldRegistry {
    public static void Register(ManagedFieldDescriptor d);                 // boot-time, by a module registrar; validates StorageName
    public static IReadOnlyList<ManagedFieldDescriptor> ForType(Type t);   // Type-plane memoized (applicability cached); frozen after first read
    public static bool IsEmpty { get; }                                    // the zero-overhead off-path gate
}
```

Append-only at boot, **frozen at first read**, Type-plane memoized. The static index is justified (the
serializer's contract-resolver needs static reach — no DI scope deep in serialization); documented as a
deliberate deviation in DATA-0105 §4 (§8). `Register` **fails closed at boot** if `StorageName` is not
naming-strategy-stable.

### Seam 2 — write injection (the record-shaping half, per serialization stack)

- `ManagedFieldWriteScope` (AsyncLocal, `Koan.Data.Abstractions`) carries the **per-op snapshot**
  `{ storageName → value }`. The chokepoint sets it **immediately before the inner write and restores it in a
  `finally`** (set-around-inner-call); the serializer reads only this snapshot, never live ambient (honors
  "snapshot ambient once"). It must survive the adapter's own no-such-table ensure-and-retry
  (`SqliteRepository.cs:802-812`) — the scope is set across the whole inner call. AsyncLocal is copy-on-write
  per async context and Newtonsoft serialize is synchronous, so concurrent ops do not bleed.
- `ManagedFieldContractResolver` (`Koan.Data.Relational`, beside `ComparableScalarEncoding`) — **one class
  parameterized by a base naming strategy**, configured as **two instances** (PascalCase/`DefaultContractResolver`
  base for SQLite & PG which today use Newtonsoft's shared global default resolver; CamelCase base for SqlServer
  which already sets its own). It appends a synthetic `JsonProperty` per applicable managed field whose value
  provider reads `ManagedFieldWriteScope.Current`. The `PropertyName` is set directly (a directly-set
  `PropertyName` in `CreateProperties` is not re-processed by the naming strategy on write); read-path stability
  is guaranteed instead by the leading-underscore convention (§7). Wired into the trio's `_json` settings (3
  one-line additions). **Mongo** injection is a registered `IBsonSerializer`/post-serialize `BsonDocument`
  element-add over `MongoRepository.cs:452` (designed in §6, landed in phase 5); **JSON-file** uses its own
  CamelCase Newtonsoft settings (phase 5).
- Round-trip is clean: the managed key is unknown to `TEntity` and deserializes away via
  `MissingMemberHandling.Ignore`.

### Seam 3 — read resolution (the filter half, one shared change)

- `ResolvedField` gains `bool IsManaged` + `string? StorageName`. `FieldPathResolver`, **gated on
  `ManagedFieldRegistry.IsEmpty`** (byte-identical throw-path + success-only memoization when empty), consults
  `ForType(rootType)` before throwing: a segment matching a registered managed field resolves to a managed
  `ResolvedField` (empty member chain, `LeafType`/`ComparableType` from the descriptor, `StorageName` carried).
- Because the relational translator (`SqlFilterTranslator.cs:66`) and the relational pushability splitter
  (`FilterSplitter.cs:56`) funnel through the **same** resolver, this one change makes both managed-aware. Each
  translator already keys column resolution off `field.Leaf` and falls back to JSON access for non-projected
  names → a managed `Eq` translates to `json_extract(Json,'$.__koan_tenant')` (SQLite) / `Json #>> '{…}'` (PG) /
  `JSON_VALUE([Json],'$.…')` (SqlServer) **with no translator change** (proj is always null for a managed name).
- `ResolvedField.GetValue` for a managed field must **read the write-scope/raw record, never the entity** (an
  empty member chain otherwise returns the entity itself → silent-empty). For pushdown adapters this is never
  invoked; it is the belt-and-suspenders for the in-memory path (phase 5). The load-bearing guard is §3's
  pushability assertion, not this.

## 3. Enforcement spans planes, not one gateway (the corrected model)

The facade is the universal gateway **for the relational/document repository path only.** A managed axis must
be honored — or fail closed — at **every** plane an entity can be reached through. The capability/pushability
gate is the structural backstop.

### 3.1 Repository plane — `RepositoryFacade` (`Koan.Data.Core`)

Per-Type-memoized `ManagedFieldPlan` (the applicable descriptors for `TEntity`; **empty ⇒ all paths below are
the unchanged direct calls, zero overhead**). **Every** write/delete and read member is assigned an explicit
managed behavior — none "falls through unchanged":

| Member | Managed behavior (when a plan applies) |
|---|---|
| `Query(QueryDefinition)` / `Count` | `query.Where(All(managedPredicates…, userFilter))`; pushes down. |
| `Get(id)` | → `Query(All(Eq(idField,id), managedPredicates…))` single-or-null. Wrong-axis ⇒ null (IDOR-safe). |
| `GetMany(ids)` | → `Query(All(In(idField,ids), managedPredicates…))`, re-ordered to requested ids (nulls for unowned). |
| `Delete(id)` | check-then-delete: managed-scoped `Get(id)`; if owned → `_inner.Delete(id)`, else false. |
| `DeleteMany(ids)` | resolve owned subset via managed query → `_inner.DeleteMany(owned)`. |
| `DeleteAll` / `RemoveAll` | lower to a **managed-scoped DELETE** (managed predicate in WHERE); **never** the unscoped truncate/Clear instruction. If the adapter can only truncate → **fail closed**. |
| `Upsert` / `UpsertMany` / batch | set the write-scope (stamp) **and verify**: the inner write is **conflict-aware** — the row is updated only if its existing managed fields equal the scope (§3.4); a mismatch is fail-closed. |
| `ConditionalReplaceAsync(model, guard)` | AND the managed predicate into the CAS guard; a managed entity on an adapter that cannot is fail-closed. |
| `QueryRaw` / `CountRaw` / Direct | **out of scope for the managed predicate** (§3.5) — RLS backstop; under enforce, asserted fail-closed-or-RLS. |

### 3.2 Cache plane — the managed axis MUST enter the cache key

`CachedRepository` wraps **outside** the facade (`DataService.cs:52`); a cache hit never reaches the facade's
predicate. `CachedRepository.TryBuildEntityKey` (`CachedRepository.cs:333-340`) and `CacheKey.For` build the
key from `{Id, Key, TypeName, Partition, Source}` with **no managed axis** → a `[Cacheable]` tenant-scoped
`Get(id)` would serve tenant A's row to tenant B. **Fix:** the cache key incorporates every registered managed
field's current value (the ARCH-0096 cache-key particle; ARCH-0095 §6 "tenant rides into cache keys"). This is
phase-3d. The no-leak proof exercises a `[Cacheable]` tenant-scoped entity.

### 3.3 Vector plane — fail closed (v1)

`Vector<T>.Search` is served by a separate `VectorService` (`VectorService.cs`) that **never** wraps the
facade, and the vector `FilterSplitter.Split(filter, caps)` overload (`FilterSplitter.cs:79`) **never** calls
`FieldPathResolver` — so neither the predicate nor the resolver change reaches it. **v1: a tenant-scoped
`[Embedding]` entity on a vector adapter that does not announce row-isolation throws** (fail closed,
boot-reported) — never a silent cross-tenant KNN. Realizing isolation (register the managed predicate into the
vector metadata-filter path, or per-tenant collection keys — ARCH-0095 §6) is a named follow-on, not v1.

### 3.4 Write verify (not deferred) — conflict-aware upsert

`Upsert` runs `_writePlan.ApplyAll` then `_inner.Upsert`, never the read filter; the relational write conflicts
on `Id` alone (`SqliteRepository.cs:822`) → a blind upsert of another tenant's id takes over and re-stamps the
row. **v1 requires verify-on-update.** Mechanism (generic, reads the managed write-scope — not tenant-aware):
the relational upsert appends a managed-conflict guard —
`… ON CONFLICT(Id) DO UPDATE SET Json = excluded.Json WHERE json_extract(Json,'$.__koan_tenant') = @scopeVal` —
and the chokepoint treats *0 rows affected on a non-insert* as a fail-closed mismatch. Emitted only when the
write-scope is non-empty (off ⇒ byte-identical). Demonstrated on SQLite in phase 4; per-adapter in the fan-out.
(The adapter-agnostic fallback — a chokepoint owned-count vs unscoped-existence check — is noted for adapters
that cannot express a conflict guard.)

### 3.5 Raw / Direct plane — out of scope, RLS backstop (named)

`DirectSession.*` and `QueryRaw`/`CountRaw` build raw commands with caller SQL and **no** managed predicate —
fully open cross-tenant surfaces. The managed predicate is **not** their defense; **RLS is the backstop**
(ARCH-0095 §5/§6). RLS + session-reset is its own phase (the connection-broker / P6 work) and lands **with the
tenancy capability ship**, not after. Until then these paths are documented-open and the no-leak proof asserts
they are **fail-closed-or-RLS under enforce**, never silently open.

### 3.6 The structural fail-closed gate (the invariant that makes the read-filter sound)

A managed predicate that lands as a **residual** (not pushed down) is evaluated in-memory by
`ResolvedField.GetValue`, which for an empty member chain returns the entity itself → `Eq(entity,'acme')` is
always false → silent-empty (and an outright leak on any path that skips the residual). Therefore: after
`FilterSplitter.Split`, **assert the managed conjunct landed in Pushable**; if not, throw the boot-reported
fix-naming error — the *same* fail-closed condition as "RequiredCapability missing." The capability gate
requires **all** of: the adapter announces the isolation capability (§4), is an `IQueryRepository`, and pushes
scalar `Eq` on the managed field. (Belt-and-suspenders: a correct managed `GetValue` per Seam 3.) For the
relational trio today `Eq` is always pushable, so this is unreachable now — it is the structural guarantee, not
a today-leak.

## 4. The capability token + tenant registration (`Koan.Tenancy`, phase 4)

The token names the **adapter capability**, not the consumer — `DataCaps.Isolation.RowScoped` (a row-level
discriminator-isolation guarantee), mirroring `Write.ConditionalReplace`. It lives in `Koan.Data.Abstractions`
and stays axis-free (`grep -i tenant|tenancy` over Abstractions stays 0). Adapters that isolate announce it.

Tenant registration is pure registration in `KoanAutoRegistrar.Initialize` (Reference = Intent):

```
ManagedFieldRegistry.Register(new ManagedFieldDescriptor(
    StorageName: "__koan_tenant",                       // leading underscore ⇒ camel-case-stable (§7)
    ClrType: typeof(string),
    ValueProvider: () => Tenant.Current?.Id,            // null under None()/unset ⇒ no field, no predicate
    AppliesTo: t => !IsHostScoped(t),                   // the existing TenantScopeMetadata cache
    RequiredCapability: DataCaps.Isolation.RowScoped,
    Indexed: true));
```

The existing `TenantStorageGuard` (presence gate, Off/Warn/Enforce) runs first; the managed field adds the
actual stamp + filter + verify. Host/unset ops inject and filter nothing — `[HostScoped]` and tenancy-Off are
zero-regression by construction.

## 5. Phase plan (refined; blockers folded)

- **Phase 3a** — converge column-name + exclusion resolution into `ProjectionResolver` (`[Column]` +
  property-`[StorageName]` for name; `[NotMapped]` + `[IgnoreStorage]` for exclusion); **delete** the dead
  `src/Koan.Data.Relational/Schema/` cluster (8 files, zero callers). Byte-identical (no entity uses the
  property-level Koan attributes).
- **Phase 3b** — the managed-field seam (Seams 1–3 + the full chokepoint surface §3.1 + the §3.6 fail-closed
  gate + conflict-aware upsert §3.4 on SQLite). Proven with a **generic (non-tenant) descriptor** so the seam
  is validated independent of tenancy. Per-adapter round-trip regression: an entity with no managed field
  registered serializes byte-identically to pre-change output.
- **Phase 3c** — schema-column DDL: the orchestrator reads `Indexed` descriptors from **`ManagedFieldRegistry`**
  (not `ProjectionResolver`, which enumerates only CLR properties) and emits a computed/expression index over
  the JSON-access expression (PG: index over `Json #>> '{…}'`; SqlServer: PERSISTED computed column over
  `JSON_VALUE` + index). SQLite stays JSON-only.
- **Phase 3d** — cache-key axis: `CachedRepository.TryBuildEntityKey` + `CacheKey.For` incorporate registered
  managed fields (the ARCH-0096 cache-key particle).
- **Phase 4** — register the tenant descriptor + `DataCaps.Isolation.RowScoped` + vector fail-closed.
  **SQLite `AssertNoTenantLeak` proof** (no-Docker, real `AddKoan()`): two tenants; cross-context read returns
  only own rows; get-by-id across tenants ⇒ null (IDOR); cross-tenant delete is a no-op; **cross-tenant Upsert
  takeover is rejected** (§3.4); **RemoveAll/DeleteAll scoped** (no cross-tenant wipe); a **`[Cacheable]`**
  tenant-scoped entity isolates (§3.2); raw/Direct asserted fail-closed-or-RLS (§3.5); `[HostScoped]`
  unaffected; **tenancy-Off zero regression**; mutation-checked (drop the predicate ⇒ a leak test goes red;
  drop the id-reorder ⇒ GetMany test goes red).
- **Phase 4 fan-out** (Workflow) — PG/SqlServer/Mongo `AssertNoTenantLeak` + conflict-aware upsert per adapter
  + computed-column indexability.
- **Phase 5** — classification (2nd module, same seam) + Mongo BSON & JSON-file write/read injection + the
  in-memory managed `GetValue` + encrypt/tokenize transforms.

## 6. Mongo write injection (designed now; landed phase 5)

Mongo has no Newtonsoft pipeline (`MongoRepository.cs:452` `ReplaceOneAsync(model)`). The managed field is
injected by serializing the model to a `BsonDocument`, adding the managed elements from the write-scope, and
replacing the document — via a registered `IBsonSerializer`/`IBsonSerializationProvider` or a thin
post-serialize element-add over the driver's `ReplaceOne`/insert path. Reads filter on the BSON element by the
literal name. Tenancy is **unshippable on Mongo** without this — so it is designed now, implemented in phase 5.

## 7. The leading-underscore `StorageName` invariant (the write/read literal must match)

On SqlServer the read path camel-cases **every** filter leaf unconditionally
(`ResolveColumnSql`/`BuildJsonAccessor`). The write literal must therefore be a **fixed point of
`CamelCaseNamingStrategy.GetPropertyName(name, false)`** — in practice it must lead with `_` (or be all
lowercase). `__koan_tenant` is such a fixed point. `ManagedFieldRegistry.Register` **enforces this at boot**
(fail closed if a future axis picks a PascalCase storage name). This single convention keeps the write literal
and the read literal identical across all adapters — it is the reason the round-trip is sound, not
`hasSpecifiedName`.

## 8. Errata + reconciliation (canon hygiene)

- **DATA-0105 §2/§8** (the "sibling column" record-field-injection premise) → **erratum**: relational
  record-field injection is realized via the Serialize/ContractResolver hook into the Json envelope, with the
  indexed sibling/computed column as an optional **Schema-stage** optimization. The `IWriteStamp`
  record-field-injection *sub-shape* is therefore unexercised in v1 (reserved for a future POCO-shaped concern);
  advancing the Serialize hook for relational from phase 5 to phase 3b is a deliberate, internally-consistent
  phase reorder.
- **tenancy-design.md §12** ("a column alongside id") → **erratum**: the discriminator is a framework-managed
  field in the persisted record (Json/BSON/sibling key) + an optional indexed computed column; add a
  "managed-field" entry to the §14 settled forks.
- **Contributor-model reconciliation** (DATA-0105 §4): the `ManagedFieldDescriptor` **is** the §0 contributor
  pattern for non-POCO ambient fields — descriptor-as-data, registered at boot by the owning module's registrar
  (like `IStorageGuard`). The static `ManagedFieldRegistry` (vs DI resolution) is a **declared deviation**
  justified solely by the serializer's static reach; documented in DATA-0105 §4 so it is not a silent parallel
  mechanism.
- **ARCH-0084**: the `RequiredCapability` "announce-or-fail-closed" is **new machinery** on top of ARCH-0084's
  declare/negotiate/self-report — stated as new, not overclaimed as existing.

## 9. Why this is "fewer but more meaningful parts"

One **read-resolution** mechanism (the shared `FieldPathResolver`) serves tenant, classification, and any
future ambient axis on the repository path; the write half is per serialization stack but driven by **one**
descriptor model; the cache and vector planes honor the **same** registered descriptors. Adapters learn one
generic concept — a registry-driven managed field — not N axes. The descriptor-as-data shape makes the no-leak
proof **structural** (assert the descriptor is registered + the predicate Pushable + the verify guard present)
and Adapter-Forge-conformance-checkable, not only behavioral fuzz.
