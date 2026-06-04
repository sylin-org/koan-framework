# DATA-0100: Comparable-Encoding Contract — order-preserving storage for filterable scalars

**Status**: Proposed (2026-06-04). Supersedes the external "DateTimeOffset adapter-capability + SDTO" proposal (`docs/design/DATETIMEOFFSET-CAPABILITY-PROPOSAL.md`).
**Date**: 2026-06-04
**Deciders**: Enterprise Architect
**Scope**: How scalar values that can be **filtered or sorted** are encoded to each storage provider so that the **store-native ordering equals the CLR ordering**. Establishes one invariant enforced at the serialization boundary, and canonicalizes the two types that violate it today — `DateTimeOffset` (→ UTC instant) and `TimeSpan` (→ Int64 ticks). Declines the proposed per-adapter type-capability surface, the `SDTO` value struct, and the filter-pipeline rewrite.
**Related**: **DATA-0098** (identity-encoding codec — sibling: that ADR made write↔query encoding *identical*; this one makes that identical encoding *order-preserving*) · DATA-0096 (unified filter pipeline) · DATA-0092/0093 (sort contract) · ARCH-0084 (unified capability model — why `FilterSupport` stays operator-level) · supersedes the gposingway DateTimeOffset proposal.

---

## Context

DATA-0096 pushes scalar comparisons (`<`, `<=`, `>`, `>=`) down to each adapter. A pushed **range** comparison is only correct if the value's **stored representation orders the same way the CLR value does**. DATA-0098 guaranteed the write path and the query comparand use the *same* encoding (no drift); it did **not** guarantee that encoding is **order-preserving on the store**. A dogfeed run (Koan.Jobs `JobOrphanReaper`, `LeasedUntil < now`) surfaced the gap on Mongo.

Empirical verification (driver `MongoDB.Driver` 3.8.1, MongoDB 7, `Newtonsoft.Json` 13.0.4 — standalone probes, bypassing Koan):

| Type | Mongo default | Mongo `$lt` order | Relational JSON-text order |
|---|---|---|---|
| `DateTimeOffset` | `Document {DateTime,Ticks,Offset}` | correct **only by accident** — `DateTime` (UTC instant) is field 0; field-by-field lexicographic otherwise | ISO-with-offset: correct **only if UTC-uniform**, wrong for mixed offsets |
| `TimeSpan` | **`String` `"1.00:00:00"`** | **WRONG** (`1day` sorts before `23h`) | **WRONG** (same string, non-monotonic) |
| `DateOnly` | `DateTime` (midnight UTC) | correct | `"yyyy-MM-dd"`: correct |
| `TimeOnly` | `Int64` (ticks) | correct | `"HH:mm:ss"`: correct |

Relational adapters (SqlServer/Postgres/SQLite) store the entity as a `[Json]` document and resolve filters by extracting from JSON; numerics/enums/bool are **cast** to a SQL family (correct), while `DateTime`/`DateTimeOffset`/`TimeSpan`/`DateOnly`/`TimeOnly` are compared **as text** (correct only when the canonical string is lexicographically monotonic).

So the genuinely cross-adapter, order-preservation holes are exactly **two**: `TimeSpan` (any range filter, on Mongo *and* relational) and **mixed-offset `DateTimeOffset`** (relational text; Mongo "by accident"). Both are latent today (no entity range-filters either), but cheapest to close before 1.0.

The original throw (`DateTimeOffset` → `BsonValue.Create` → `ArgumentException`) is a *fidelity* failure, already fixed by the kept `Nullable<T>` unwrap in `MongoFilterTranslator.Encode`; it is orthogonal to ordering and stays.

### Forces

1. **The framework already lives by the right invariant — implicitly.** Enums persist as **strings on Mongo** but **numbers on Postgres**, and both compare correctly. Per-adapter encoding is already accepted *as long as each encoding is internally order-preserving*. Make that rule explicit; close its two gaps.
2. **All adapters are document/JSON stores.** None use a native temporal column (SqlServer stores `[Json]`, not a `datetimeoffset` column — DATA `97779228`). A "Native / InstantOnly / None" type-capability matrix therefore models a distinction Koan **does not have**.
3. **Greenfield, < 1.0.** No durable data to migrate ⇒ set the canonical contract now. A breaking encoding change is cheap now and expensive after 1.0.
4. **Fewer, more meaningful parts.** A value struct + a pipeline-rewrite pass + a per-adapter type-capability token are *more* moving parts. One invariant enforced at the **existing** encoding points is *fewer* — and it replaces "works for some types, silently breaks for others" with one stated contract.
5. **Offset preservation has no consumer.** Every persisted `DateTimeOffset` in the codebase is `DateTimeOffset.UtcNow` (offset 0). "Store the instant; model the zone explicitly if ever needed" is mainstream and reversible.

---

## Decision

**The comparable-encoding contract:** *every property that can be filtered or sorted must persist, on each adapter, in a representation whose store-native ordering equals its CLR ordering.* Per-adapter encodings remain free to differ; each must be order-preserving. Enforcement lives at the **serialization boundary** — the same single-owner places DATA-0098 established — never via a capability surface, a value struct, or a filter-AST rewrite.

### 1. Instants, not offsets — `DateTimeOffset` persists as a UTC instant

The offset is **not** part of the persisted scalar contract. An application that needs the original zone models it explicitly (a sibling field). This is order-preserving, lossless for the *instant*, and removes a composite type from the scalar-storage model — which is precisely why `SDTO` and the tri-state are unnecessary.

- **Mongo**: register `DateTimeOffsetSerializer(BsonType.DateTime)` (+ the `Nullable<DateTimeOffset>` variant) — a top-level `BsonDate`; `$lt/$gt` are primitive, correct, indexable.
- **Relational**: a `JsonConverter` writes `value.ToUniversalTime()` in a fixed, round-trippable, UTC (`…Z`) ISO-8601 form so JSON-text comparison is monotonic; the filter comparand is encoded identically.

### 2. Durations as ticks — `TimeSpan` persists as `Int64` ticks

- **Mongo**: register `TimeSpanSerializer(BsonType.Int64)` (+ nullable variant).
- **Relational**: a `JsonConverter` writes ticks as a JSON **number**; `TimeSpan` joins the numeric cast family (`::numeric` / dialect equivalent); the comparand renders as the tick number. Write-encoding and comparand-encoding move together.

### 3. Already-comparable types are unchanged

`DateOnly` (Mongo `DateTime`; relational `yyyy-MM-dd`) and `TimeOnly` (Mongo `Int64`; relational fixed `HH:mm:ss`) are order-preserving as-is. **No code change** — only conformance tests that prove it and lock it.

### 4. The comparand uses the same encoding as the write (DATA-0098)

`MongoFilterTranslator` already encodes the comparand through the field's own serializer; once §1/§2 register the canonical serializers, the comparand follows automatically. `FilterValueConverter` is **unchanged** — it only normalizes the comparand's CLR *type* (returns a `DateTimeOffset`/`TimeSpan` as-is); it never decides storage shape.

### 5. `FilterSupport` stays operator-level (ARCH-0084)

Comparability is a property of the **stored encoding**, not a per-adapter **operator** capability. It is *not* modeled as a capability token. There is no `AdapterTypeSupport`. This keeps "the one structured capability detail in the framework" exactly that.

### 6. Fail-loud validation — Phase 2 (follow-up)

The contract's enforcement is the encoding registry itself. As a follow-up, a boot-time assertion can verify that every filterable/sortable property type has a registered order-preserving encoding for the active adapter, and fail loud (or force the predicate to the in-memory floor) for an unknown value-object type — mirroring `FilterSupport`'s "negotiation fails loud" philosophy, but as one assertion over the one registry, not a per-adapter type-capability set. Documented here; not implemented in this change.

---

## Consequences

### Positive
- The **order-preservation bug class is structurally closed** for the canonicalized types across every pushdown adapter. Correctness no longer rests on the Mongo driver emitting `DateTime` first, or on data happening to be all-UTC.
- **Strictly fewer parts** than both the status quo (implicit, accidental) and the proposal (capability token + value struct + rewrite pass). Reuses DATA-0098's single-owner boundary; comparand parity is preserved for free.
- The contract generalizes: any future composite scalar gets one canonical, order-preserving encoding at the boundary — no new abstraction.

### Negative
- `DateTimeOffset` **loses its original offset** on persist. Decision, not oversight; reversible via an opt-in lossless encoding scoped to specific fields if a real need ever appears — and *that*, scoped and consumer-driven, is the only circumstance where an `SDTO`-shaped type earns its place.
- **Breaking encoding change** for any data written under the old shapes (Mongo `Document`→`Date`, `TimeSpan` string→`Int64`; relational `TimeSpan` string→number). Pre-1.0: re-seed. **Flagged.**
- On Mongo, `DateTimeOffset` as `BsonDate` is millisecond precision. This is **not** a comparison regression (the old Document's comparable field, `DateTime`, was already ms); it drops the old `Ticks` round-trip field, so sub-ms tick round-trip is no longer preserved on Mongo. Immaterial for current usage; noted.

### Neutral
- A live per-adapter ordering-conformance test becomes mandatory for the canonicalized types (ARCH-0079).

---

## Tests

- **Live per-adapter ordering matrix (ARCH-0079)** across Mongo, Postgres, SqlServer, SQLite, with **InMemory as the CLR oracle**: `{ DateTimeOffset (incl. mixed-offset inputs), TimeSpan (incl. across-the-day-boundary: 90m, 2h, 23h, 24h, 48h), DateOnly, TimeOnly } × { write→read round-trip equals the input instant/duration, range filter <,<=,>,>= returns the chronological/duration-correct set, ascending sort is in the correct order }`. The mixed-offset and across-day-boundary rows are the regression anchors — they fail on the pre-change encodings.
- **Reaper regression (Jobs)**: expired-lease `Running` orphans are reaped against a real adapter.
- **No-Docker wire-shape units** extended to assert the new Mongo shapes (`DateTimeOffset` → `$date`, `TimeSpan` → `$numberLong`).

## Migration
Dogfeed: re-seed. Deployed data: a one-time conversion (Mongo `Document`→`Date`, `TimeSpan` string→ticks; relational `TimeSpan` string→ticks, `DateTimeOffset`→UTC ISO). Tooling out of scope for this change. **Flagged.**

## Notes for reviewers
- The **invariant is the contract**: if encoding is decided only at the boundary and is order-preserving, pushed range comparisons are correct by construction — no rewrite, no capability negotiation.
- Out of scope (follow-ups): the boot-time validator (§6); the Couchbase N1QL realization if it encodes these types differently; the offset-preserving opt-in encoding; the deployed-data migration tool.

---

## Implementation & verification (2026-06-04)

Realized and verified green on **every pushdown adapter** via a shared oracle (`TemporalConvergence` in the AdapterSurface TestKit) that compares each adapter's range-filter and sort id-sets to the compiled C# predicate (`predicate.Compile()` / `OrderBy`) over an adversarial corpus (mixed-offset `DateTimeOffset`, across-the-day-boundary `TimeSpan`):

- **Mongo** — full suite 20/20; teeth-checked (reverting the registration makes the spec fail).
- **SQLite** — full suite 2/2 (dockerless).
- **Postgres** — full suite 8/8 against a real container.
- **SqlServer** — green against a real SQL Server 2022 container (table populated).
- **InMemory** — green (CLR floor / oracle).

Realization points: Mongo — `MongoOptimizationAutoRegistrar` registers the built-in `DateTimeOffsetSerializer(BsonType.DateTime)` + `TimeSpanSerializer(BsonType.Int64)` (and nullable variants), each guarded individually. Relational — `Koan.Data.Relational.ComparableScalarEncoding` provides the Newtonsoft converters (write) and `EncodeComparand` (consumed by `SqlFilterTranslator.AddParam`), so write and comparand are byte-identical; per-dialect `ResolveColumnSql`/cast tables apply the `TimeSpan` numeric cast for **both** filter predicates and `ORDER BY`.

### Findings that sharpened the contract (beyond the original analysis)
1. **`$eq` was actually broken on Mongo (not just `$lt` fragility).** Under the default `{DateTime,Ticks,Offset}` document, equality compares the *whole* document, so two `DateTimeOffset` values with the same instant but different offsets do **not** compare equal. The convergence oracle caught this (`dto-eq-same-instant`) where the static `$lt` analysis had not. Canonicalizing to the UTC instant fixes both.
2. **The relational comparand is bound by the ADO.NET driver, not Newtonsoft** — write encoding and comparand encoding are decoupled. Empirically, `DateOnly`/`TimeOnly` comparands **throw** (`cannot be used as a parameter value`), and `DateTimeOffset`/`TimeSpan` comparands bind in a form that does not match the stored JSON text. The fix is to encode the comparand to the canonical store form (`EncodeComparand`) before binding; this is what makes the contract hold on relational at all.
3. **`DateParseHandling.None`** is set on the relational settings so Newtonsoft delivers raw string/number tokens to the converters (rather than pre-parsing ISO strings to `DateTime` under the ambient `DateTimeZoneHandling`), making the round-trip deterministic and avoiding silent coercion of string-typed members.
4. **Sort must use the same cast as filters.** `ORDER BY` resolves the field through `FieldPathResolver` so the `TimeSpan` numeric cast is applied to the sort column too (otherwise duration sorts lexicographically).
5. **Inherited (base-declared) members lost their serializer on Mongo — the actual §2.3 reaper residual.** `MongoFilterTranslator.ResolveScalarSerializer` used `BsonClassMap.GetMemberMap(name)`, which returns only members **declared** on the looked-up class. For a `Job<T> : Entity<T>` CRTP hierarchy the filtered members (`LeasedUntil`, etc.) live on the abstract base, so the lookup returned null, the comparand fell to `BsonValue.Create`, and the reaper threw the *original* `DateTimeOffset cannot be mapped to a BsonValue` **even after** the storage fix landed. This is a class-map-lookup defect, not a storage one. Fixed by resolving through `AllMemberMaps` (inherited-aware), **memoized** per `(type, member)` so the per-comparison translation path stays O(1) with no allocation. The convergence oracle's entity was converted to a CRTP hierarchy (`TemporalWidget : TemporalWidgetBase<TemporalWidget>`) to cover it — a flat entity never exercised this path; with the fix reverted the spec reproduces the throw for all four governed types. **Rejected** the alternative "registry fallback in `Encode`" (a previously-reverted guard): post-contract it would encode `DateTimeOffset`/`TimeSpan` correctly from the global registry but would silently mis-encode a base-declared **enum** (drift to its int ordinal vs the stored string) and GUID refs — re-introducing the DATA-0098 drift class. The root-cause fix preserves each field's *configured* serializer for every type.

### Pre-existing issue surfaced (NOT changed here)
`PostgresRepository.ToRowOptimized` serializes the entity to JSON *before* calling `OptimizeEntityForStorage`, so the `Id` inside the JSON document can differ from the optimized row key (SQLite/SqlServer optimize first). Independent of this contract; flagged for a separate fix.
