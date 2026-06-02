# DATA-0098: Identity Encoding Codec — declared, per-field, single-owner

**Status**: Accepted (2026-06-01) — design approved; supersedes the value-sniffing `SmartStringGuidSerializer`.
**Date**: 2026-06-01
**Deciders**: Enterprise Architect
**Scope**: How string-typed identity values — an entity's `Id` and references to it — are encoded to/from each storage provider's optimal native type. Replaces the global value-sniffing string serializer in `Koan.Data.Connector.Mongo` with a declared, per-field codec; defines a provider-agnostic selection contract realized per adapter.
**Related**: DATA-0096 (unified filter pipeline) · DATA-0097 · the three regression fixes `c8905ed9` / `68bc4257` / `1c974d57` · **supersedes** the value-based `SmartStringGuidSerializer` (archived `mongodb-guid-optimization` note).

---

## Context

The approved DX goal: a developer models identity as `string` everywhere — `Entity<T>.Id` and references — and the framework stores it in the provider's optimal native form (Mongo UUID `BinData`, relational `uuid`) **at the boundary**. That delivers three things, all real:

1. **Boundary asymmetry** — ids are strings in URLs / JSON / routes / logs, optimal on disk; no `Guid.Parse(routeId)` leaking the storage choice into the app.
2. **Identity polymorphism** — switch an entity GUID → slug → content-hash by changing its id strategy, not its model fields or any consumer.
3. **No `Guid?` pollution** — references stay `string?` (NRT ergonomics) instead of struct-nullable `Guid?` spreading through the domain.

The first implementation pursued this with a serializer registered **globally for `typeof(string)`** that decided **per runtime value** (`Guid.TryParse`). In one dogfeed run that mechanism produced three silent-data-loss bugs:

- write/read drift on `List<string>` elements → required the `StringCollectionElementConvention` carve-out;
- a bind-time crash on interface-typed string collections (`c8905ed9`);
- a write↔query encoding asymmetry on a scalar Guid-string FK, compounded by an emission-layer `{_v}` discriminator wrap (`68bc4257` + `1c974d57`) — `Query(s => s.PackageId == id)` returned 0 rows moments after the write, and a delete-when-empty caller dropped the aggregate.

**Root cause (one):** the encode decision was made **per runtime value, in N independent places that each re-derived it** (serializer, filter translator, collection-element path, interface binder, emission layer). Any one deriving it differently fails silently. The value-sniff also carries two hazards beyond drift:

- **Over-reach** — *every* string field is sniffed, not just ids/refs.
- **Id mutation** — `Guid.TryParse` is liberal (accepts `N`/`B`/`P`/uppercase) but decode emits canonical `ToString("D")`, so a non-canonical guid-shaped id silently reformats on round-trip (e.g. a 32-char no-dash id comes back 36-char dashed) — lookups by the original string then fail.

### Forces

1. **The goal is right; the value-sniffing mechanism is the defect.** Keep the goal, replace the mechanism.
2. **The strategy is already known statically.** `StorageOptimizationExtensions.GetStorageOptimization<T,K>()` already encodes the id strategy (`Entity<T>` → Guid, `Entity<T,string>` → None, `[OptimizeStorage]` override, cached, SP-independent), and `IRelationshipMetadata.GetParentRelationships(type)` maps a field to its parent entity type.
3. **Drift dies only if write and query select from the SAME static metadata** — not by each re-deriving from the value.
4. **Correctness must not depend on the optimization** — an undeclared guid-shaped string must round-trip losslessly.

---

## Decision

**Identity encoding is a declared, per-field decision with a single owner**, computed from static metadata that the write path and the query path consult identically.

### 1. Selection — one function, the single source of truth

A field is GUID-encoded **iff**, for its owning entity type `E` and property `P`:

- `P` is `E`'s Id property **and** `GetStorageOptimization(E)` is `Guid`; **or**
- `P` is a parent reference (`[Parent(typeof(Pn))]` → `GetParentRelationships(E)`) **and** `GetStorageOptimization(Pn)` is `Guid`.

Otherwise `P` is a plain string. This is a pure, cached function of `(E, P)`, exposed as one API — `IdentityEncoding.IsGuidEncoded(entityType, property)` / `GuidEncodedMembers(entityType)`. **Both the write serializer registration and the filter translator call it; they cannot disagree.**

### 2. Codec — one owner of the round-trip

A codec owns `Encode` (used on **write and as the query comparand — the same encoding**) and `Decode` (read). The GUID codec: string → UUID `BinData` (`Standard`); `BinData` → canonical `"D"` string. **There is no separate query-comparand path** — the filter uses the same `Encode`, which is what makes write↔query drift impossible. Scalar-vs-collection-element is a *selection* difference (an element position is not an id/ref, so it takes the string codec), not a per-codec write/query split.

### 3. Write path — per-member registration, no global override

The Mongo auto-registrar registers the GUID-codec serializer on exactly the GUID-encoded members (the Id + guid parent-refs) via `BsonClassMap`, and **removes the global `BsonSerializer.RegisterSerializer(typeof(string), …)` override**. Every other string keeps the default serializer.

### 4. Query path — serialize the comparand through the field's own serializer

`MongoFilterTranslator` encodes each scalar comparison value by running it through the **member serializer** the class map exposes for that field (`BsonClassMap.LookupClassMap(entity).GetMemberMap(prop).GetSerializer()`) — the SAME serializer the write path uses. This subsumes the GUID case (the per-member codec from §3 → UUID BinData) and, crucially, **every other configured representation**: enums (string, via the global `EnumRepresentationConvention`), `DateTime`, `Decimal`, etc. The translator never re-derives a value's BSON form, so write↔query encoding cannot drift for *any* type.

> **Generalization note.** The first cut selected per-field GUID encoding in the query path (`IdentityEncoding` + `MongoGuidEncoding`) — which fixed GUID but left **enum** drift (`{status:"Published"}` written, `{status:1}` queried → 0 rows). Delegating to the member serializer is the general rule: the field's *configured* serializer is the single authority for its stored form. `IdentityEncoding` remains the **write-side** selection (§3, which members get the GUID codec); the query side simply reads whatever serializer that produced. Comparands are emitted as raw `BsonDocument`s so a non-primitive `BsonValue` lands top-level rather than being wrapped by `ObjectSerializer` in a `{_v:…}` envelope.

### 5. Provider-agnostic

The selection function and codec contract are provider-agnostic (the metadata is). The Mongo `BinData` codec is the first realization; a relational adapter realizes the same selection as a `uuid` column mapping in a follow-up.

### 6. Correctness over optimization

An undeclared guid-shaped string (not an Id, not a declared ref) is stored and queried as a **plain string** — unoptimized but lossless and drift-free. The optimization is opt-in via the entity's declared identity strategy and declared relationships (which entity-first already encourages).

### Migration

Collections written under the global value-sniffer stored *every* guid-shaped string (including undeclared ones) as `BinData`. After this change only declared ids/refs are `BinData`. So: (a) declared Ids + declared parent-refs **keep** `BinData` (continuity — no migration needed); (b) **undeclared** guid-strings that were `BinData` must be migrated to string, or declared as refs to keep `BinData`. Dogfeed: re-seed. Production: a one-time migration pass (documented; the tooling is out of scope for this code change). **Flagged.**

---

## Consequences

### Positive
- The **drift bug class is structurally gone** — one selection, one codec, both paths.
- **Over-reach gone** (only ids/refs are encoded); **id mutation gone** for undeclared strings (they are never parsed).
- The DX goal is fully delivered: string model, optimal storage, polymorphism, no `Guid` pollution.
- Provider-agnostic; the same selection drives every adapter's native mapping.

### Negative
- A migration for existing **undeclared** guid-string data (declared ids/refs are continuous).
- Refs must be declared (`[Parent]`) to be `BinData`-optimized — aligned with entity-first, but a behavior change vs "optimize any guid-shaped string."
- Per-entity metadata lookups (cached, negligible).

### Neutral
- A live matrix conformance test becomes mandatory for any adapter claiming identity optimization.

---

## Tests

A live, per-adapter matrix (ARCH-0079): **{guid-id entity, string-id entity, guid parent-ref, string parent-ref, string-stored enum, plain guid-shaped non-id string} × {write→read round-trip equals input, query-by-`Eq` finds it, absent returns empty}**. This asserts both that ids/refs optimize *and* that round-trips are lossless (catches the mutation hazard and the `_v`-wrap regression end-to-end). Plus the no-Docker translator wire-shape unit (already added in `MongoFilterWireShapeSpec`).

## Notes for reviewers

- The selection function **is** the contract: if it and the codec are the only places encoding is decided, drift is impossible by construction.
- Out of scope (follow-ups): the relational `uuid` realization; nested / collection-of-reference encoding; the production data-migration tool.
