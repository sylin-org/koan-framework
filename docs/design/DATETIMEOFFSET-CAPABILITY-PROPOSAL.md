# DateTimeOffset has no universal storage primitive — adapter-capability proposal

Status: proposal, awaiting Koan-side decision
Author: external (gposingway dogfeeding session)
Surfaced via: Koan.Jobs `JobOrphanReaper` failing against the Mongo adapter

## 1. Summary

`DateTimeOffset` is a composite CLR type — an instant plus a fixed UTC offset — with no native primitive in most stores (BSON, Postgres' `timestamptz`, MySQL, SQLite, JSON). Only SQL Server has a true native primitive (`datetimeoffset`). MongoDB.Driver's `DateTimeOffsetSerializer` defaults to a Document representation `{DateTime, Ticks, Offset}`, which preserves round-trip but **breaks `$lt`/`$gt` semantics**: Mongo's `$lt` on a Document field is lexicographic field-by-field comparison, not chronological date comparison. It happens to work today only because `DateTime` is the first field and is a comparable BsonDateTime — an unstated invariant that any per-member serializer change can silently invalidate.

The pattern generalizes beyond `DateTimeOffset`. `TimeSpan`, `DateOnly`, `TimeOnly`, and any custom value object that lacks a storage primitive face the same class of problem on the same set of adapters.

The proposal is to surface this as a Koan **adapter capability** (per-adapter declaration of which CLR types it stores natively) plus a **framework-level filter rewrite** that, for non-native adapters, projects predicates onto a canonical comparable subfield.

## 2. Discovery

### 2.1 Surfacing symptom

Koan.Jobs' `JobOrphanReaper` runs:

```csharp
await Job<T>.Query(j => j.Status == JobStatus.Running &&
                        (j.LeasedUntil == null || j.LeasedUntil < now),
                   cancellationToken);
```

against the Mongo adapter (`Job<T>` is `Entity<T>`-derived; `LeasedUntil` is `DateTimeOffset?` with `[Index]`). The reaper threw:

```
System.ArgumentException: .NET type System.DateTimeOffset cannot be mapped to a BsonValue.
  at MongoDB.Bson.BsonTypeMapper.MapToBsonValue(Object value)
  at Koan.Data.Connector.Mongo.MongoFilterTranslator`1.ScalarBson(...)
  at Koan.Data.Connector.Mongo.MongoFilterTranslator`1.BuildField(...)
```

### 2.2 Root cause of the throw

`MongoFilterTranslator.Encode` (the scalar-comparand encoder) takes a two-step path:

1. **Path 1 — field's own serializer.** If `ResolveScalarSerializer` returns a serializer whose `ValueType` matches the value's CLR type (`IsInstanceOfType` check), encode through it. This keeps write↔query encoding aligned per DATA-0098.
2. **Path 2 — fallback.** Otherwise: `value as BsonValue ?? BsonValue.Create(value)`. `BsonValue.Create` routes to `BsonTypeMapper.MapToBsonValue`, which only knows a fixed primitive set (`int`, `string`, `double`, `bool`, `BsonValue`, …). `DateTimeOffset`, `TimeSpan`, and custom records all throw.

For `LeasedUntil` (`DateTimeOffset?`) compared against `now` (`DateTimeOffset`):
- Field serializer: `NullableSerializer<DateTimeOffset>`, `ValueType = DateTimeOffset?`
- Comparand: `DateTimeOffset`
- `typeof(DateTimeOffset?).IsInstanceOfType(dto) == false` (nullable assignment rules: `Nullable<T>.IsAssignableFrom(T)` is false; boxed `DateTimeOffset?` boxes to a plain `DateTimeOffset` box)
- Path 1 skipped; Path 2 throws.

A narrow fix landed: unwrap `Nullable<T>` from `serializer.ValueType` before the `IsInstanceOfType` check, so the serializer is used. **That fix is kept** (it's correct for any `Nullable<T>` field — `Guid?`, custom struct?, etc. — not just DTO). It is in the current branch.

### 2.3 What the narrow fix did NOT address — the actual bug

With the unwrap in place, `Encode` no longer throws. It serializes the comparand through `NullableSerializer<DateTimeOffset>`, which produces a BSON Document `{DateTime, Ticks, Offset}` matching the stored shape. The filter becomes:

```js
{ status: "Running",
  $or: [ { leasedUntil: null },
         { leasedUntil: { $lt: { DateTime: ISODate(...), Ticks: NumberLong(...), Offset: 0 } } } ] }
```

Direct verification against the running database (50 expired-lease `CaptureJob` rows present):

```js
// Reaper-emulated query with shape-matched Document comparand:
db.getCollection("...CaptureJob").countDocuments({
    status: "Running",
    $or: [
        {leasedUntil: null},
        {leasedUntil: {$lt: {DateTime: new Date(), Ticks: Long.fromString("..."), Offset: 0}}}
    ]
})
// → 50  (matches all expired leases)

// But the running reaper isn't reaping any of them.
// "Orphan reaper recovered N stale Running job(s)" never logs (only logs when N > 0).
```

Two implications:

1. **Mongo's `$lt` on Document-shape fields is lexicographic field-by-field comparison.** It currently behaves like a date comparison purely because `DateTime` is the first field and is a comparable `BsonDateTime`. This is an unstated structural invariant. Any per-member serializer choice — a custom converter, a `Representation` change in a future MongoDB.Driver version, even field-reordering during a class-map override — silently breaks it.
2. **There is still a residual reaper issue** (the running reaper not matching even though the manual emulated query does). Investigation was paused at this point because the root architectural problem — DTO has no comparable primitive — is upstream of any concrete reaper fix. The reaper's correctness is downstream of the SDTO decision below.

### 2.4 A speculative "guard" was tried and reverted

A second translator change was proposed and committed during investigation: replace the `BsonValue.Create` fallback with `BsonSerializer.LookupSerializer(value.GetType())` to "make the fallback safe." It was **reverted** before this proposal was written, because:
- It MASKS the real issue: the throw signal was diagnostically valuable. Without it, the symptom shifts from "loud crash" to "silently wrong query results" — strictly worse for debugging.
- It does NOT fix the comparison-semantics bug: the registry-returned serializer for `DateTimeOffset` is the same `DateTimeOffsetSerializer` with the same Document representation, producing the same fragile comparison.
- The proper architectural fix below makes the fallback path irrelevant — when SDTO lands, Path 1 always matches.

The narrow `Nullable<T>` unwrap stays. The speculative fallback change is reverted.

## 3. The underlying architectural problem

`DateTimeOffset` is a composite type. Storage primitives across backends:

| Primitive | What it stores | Offset handling |
| --- | --- | --- |
| SQL Server `datetimeoffset` | instant + offset | preserved (native) |
| Postgres `timestamptz` | UTC instant | **normalized to UTC, original offset dropped** |
| MySQL `TIMESTAMP` | UTC instant | dropped (session-tz reinterpretation only) |
| BSON `Date` | UTC instant (int64 ms epoch) | no slot |
| SQLite | no native temporal type | string/int convention |
| JSON | no native date type | ISO string convention |

Driver responses (without a primitive):
- Truncate to UTC instant → offset lost.
- Serialize as ISO-8601 string → lossless, lexicographically sortable, comparable across backends.
- Serialize as multi-field document → lossless, **but breaks `$lt`/`$gt` semantics** (Mongo's current default).

The class generalizes to `TimeSpan`, `DateOnly`, `TimeOnly`, and arbitrary value objects.

## 4. Proposal

### 4.1 Capability declaration

Extend the existing per-adapter capability pattern (e.g. `MongoFilterTranslator.Capabilities = new FilterSupport(ScalarOperators: …, NestedPaths: true, IgnoreCase: true)`) with a CLR-type capability surface. Adapters declare which CLR types they handle natively for storage AND comparison:

```csharp
public sealed record AdapterTypeSupport(
    IReadOnlySet<Type> NativeTypes,
    IReadOnlySet<Type> InstantOnlyTypes);   // see §4.2 on tri-state
```

Examples:
- **SQL Server adapter**: `NativeTypes ⊇ { typeof(DateTimeOffset) }` (round-trip + comparable).
- **Postgres adapter**: `InstantOnlyTypes ⊇ { typeof(DateTimeOffset) }` (comparable, but lossy: offset dropped on write).
- **Mongo / SQLite / InMemory**: neither set contains `DateTimeOffset`.

### 4.2 Why tri-state, not boolean

A single `bool SupportsNativeDateTimeOffset` is wrong for Postgres: `timestamptz` IS a primitive, IS indexable, IS comparable — but it drops the original offset on write. Treating that as "native" silently corrupts data; treating it as "non-native" wastes the primitive and forces SDTO unnecessarily. Three states cover the matrix:

| State | Comparable as primitive? | Round-trips offset? | Examples |
| --- | --- | --- | --- |
| `Native` | yes | yes | SQL Server `datetimeoffset` |
| `InstantOnly` | yes (UTC instant) | **no** (offset dropped) | Postgres `timestamptz`, MySQL |
| `None` | no | no | Mongo (default), SQLite, JSON |

`Native` → adapter passes the filter through unchanged. `InstantOnly` → framework rewrites the predicate to compare on the UTC instant only AND warns/errors if the entity carries a meaningful non-zero offset (or the framework just normalizes to UTC at write time — call this out as a deliberate design choice with surfaced semantics). `None` → framework projects onto SDTO and rewrites predicates accordingly.

The same surface generalizes naturally to `TimeSpan`, `DateOnly`, `TimeOnly`, and custom value objects.

### 4.3 SDTO — Serializable DateTimeOffset

Original suggestion: 4 fields (`DateTime, Ticks, Offset, ConcreteDate`).

Refinement to **2 fields**, lossless and indexable:

```csharp
public readonly struct SDTO : IEquatable<SDTO>, IComparable<SDTO>
{
    public DateTime Utc { get; }            // UTC instant — the comparable primitive
    public short OffsetMinutes { get; }     // original offset

    public SDTO(DateTimeOffset value)
    {
        Utc = value.UtcDateTime;
        OffsetMinutes = (short)value.Offset.TotalMinutes;
    }

    public DateTimeOffset ToDateTimeOffset()
        => new DateTimeOffset(Utc, TimeSpan.Zero).ToOffset(TimeSpan.FromMinutes(OffsetMinutes));

    // implicit conversions to/from DateTimeOffset so consumer code never sees SDTO
}
```

Rationale:
- `Utc` + `OffsetMinutes` is lossless; the original `DateTime` (local) and `Ticks` are derivable.
- `Utc` is a BSON Date (or Postgres `timestamptz`, etc.) — indexable, primitive `$lt`/`$gt` works correctly.
- `OffsetMinutes` as `int16` is sufficient (UTC±14:00 = ±840, fits comfortably).
- Read side: implicit conversion to `DateTimeOffset` so consuming code (Jobs, entities) never sees SDTO. The reaper still writes `j.LeasedUntil < now` in CLR; storage and the filter layer translate.

### 4.4 Where the rewrite lives — framework, not adapter

The capability check and the rewrite belong in the **filter pipeline / coordinator**, not per-adapter:

1. Filter AST is built (CLR-level: `LeasedUntil < now` with both sides `DateTimeOffset`).
2. The coordinator consults the target adapter's `AdapterTypeSupport`.
3. **If `Native`**: filter is passed through unchanged to the adapter's translator.
4. **If `InstantOnly`**: filter is rewritten so the comparison operates on the UTC instant projection (`LeasedUntil.Utc < now.UtcDateTime`). Both sides are now primitive Date values; the adapter sees a vanilla scalar comparison.
5. **If `None`**: same rewrite, with the storage shape now an embedded SDTO sub-document (or two top-level columns, adapter's choice). Adapter sees `LeasedUntil.Utc < now.UtcDateTime` — primitive on both sides.

The adapter's translator code stays simple — it only handles primitives it has always handled. All the DTO-specific knowledge lives in one place.

This mirrors Koan's existing layering: `Capabilities` already drive operator-level routing (a non-pushable operator falls back to the in-memory floor); type-level routing is the same pattern at a different granularity.

### 4.5 Write-side invariant

The redundant comparable field is only safe if **every write path keeps it in sync**. Recommendations:

- Make SDTO an opaque CLR struct. No public setters on `Utc` or `OffsetMinutes` independently — only constructible from a `DateTimeOffset`. This eliminates partial-update drift from CLR-level callers.
- Mark SDTO `Utc` as the indexable primary; SDTO's BSON/SQL serializer always recomputes both fields together on write.
- Document the structural invariant: any direct-DB mutation (mongosh edits, migrations, raw SQL) is the caller's responsibility to maintain consistency. The framework can validate on read in DEBUG builds if drift becomes a recurring issue.

## 5. What this proposal does NOT cover (deferred)

- **Migration of existing data.** Mongo databases already in the field have the legacy `{DateTime, Ticks, Offset}` shape. Whether this is handled by a one-shot migration, by SDTO's reader accepting the legacy shape, or by a hard cutover, is a separate decision.
- **Specific Postgres semantics.** Whether `InstantOnly` adapters should warn, throw, or silently normalize on a non-zero offset write is a deliberate design call.
- **`TimeSpan` / `DateOnly` / `TimeOnly` rollout.** Same architectural shape, same framework rewrite, separate concrete encodings. Recommended to land DTO first as the proof, then extend.
- **The residual reaper-not-reaping behavior** noted in §2.3 — paused pending the architectural decision above, since any concrete reaper fix is downstream of SDTO.

## 6. Current state of the branch

In Koan (`feat/unified-filter-pipeline`):

- `9a6e02ca fix(jobs+mongo): lease+reaper, pre-semaphore I/O move, nullable-DTO filter` — KEPT.
  Includes the `Nullable<T>` unwrap in `MongoFilterTranslator.Encode`, the lease+reaper substrate in Koan.Jobs, and the pre-semaphore Mongo I/O move in `JobDispatcher`. The unwrap is a narrow, general-purpose correctness fix for any `Nullable<T>` field comparison (not DTO-specific); it stays.
- `a17c9c89 fix(mongo): robust value-encode fallback via SerializerRegistry` — REVERTED (`2b03c9ff`).
  This was the speculative guard described in §2.4. Removed because it masks the real bug and the SDTO design makes it unnecessary.

In gposingway:
- The `Koan.Data.Connector.Mongo` reference was bumped 0.17.7 → 0.17.8 alongside the guard; that bump is REVERTED (`7fd07393`), keeping 0.17.7 (the version with the nullable-unwrap only).

Net effect on the framework: the only translator change still in-branch is the `Nullable<T>` unwrap. No "guard" code remains.

## 7. Open questions for the Koan specialist

1. Is the per-adapter type-capability surface (§4.1) consistent with how `FilterSupport` / capability detection already works elsewhere in the framework? Any precedent for type-level capability we should align with?
2. Tri-state (§4.2) vs. two orthogonal booleans (`PreservesOffset`, `ComparablePrimitive`) — preference?
3. SDTO field count (§4.3): 2 fields (`Utc`, `OffsetMinutes`) is the proposal here; the original ask was 4 (`DateTime, Ticks, Offset, ConcreteDate`). Is there a Koan reason to keep the wider shape (debug visibility, audit logs, etc.) that I'm missing?
4. The filter rewrite (§4.4): does Koan's existing filter pipeline have a natural extension point for type-level rewrites, or does this need a new pass?
5. The residual reaper-not-reaping (§2.3) — worth a separate investigation pass after the architectural decision, or expected to dissolve once SDTO lands?

## 8. References

- Koan.Jobs reaper code: `src/Koan.Jobs.Core/Execution/JobTypeRegistry.cs` (`JobTypeOps<T>.ReapOrphansInto`)
- Mongo filter translator: `src/Connectors/Data/Mongo/MongoFilterTranslator.cs` (Encode, ResolveScalarSerializer)
- Mongo auto-registrar (entity class-map registration): `src/Connectors/Data/Mongo/Initialization/MongoOptimizationAutoRegistrar.cs`
- DATA-0098 (DTO-related serialization contracts)
