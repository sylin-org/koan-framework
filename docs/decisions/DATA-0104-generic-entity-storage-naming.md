# DATA-0104: Recursive generic-entity storage-name grammar

**Status**: Accepted (2026-06-21)
**Date**: 2026-06-21
**Deciders**: Enterprise Architect
**Scope**: How the framework derives a physical storage identifier (table / collection / key / index name) for a **generic-over-entity** type — e.g. the AI pillar's `EmbeddingState<TEntity> : Entity<EmbeddingState<TEntity>>`. Defines one recursive rule at the single naming chokepoint and the cross-adapter conformance contract that keeps it honest.
**Related**: **ARCH-0084** (capability model — adapters announce, the framework composes) · **ARCH-0079** (integration tests as canon — the conformance oracle) · **DATA-0077 §4** (`PartitionNameValidator` — the identifier alphabet) · the storage-naming chokepoint (`StorageNameGenerator` / `StorageNameResolver`).

---

## Context

A type that is generic over an entity — the canonical case is `EmbeddingState<Todo>`, persisted by `[Embedding(Async = true)]` — had **no** naming rule. It fell into the namespace style branches of `StorageNameResolver.Resolve`, which read `Type.Name` / `Type.FullName`. For a closed generic those mangle:

- `Type.Name` → `EmbeddingState`​`` `1 `` — the **type argument is dropped**, so *every* `EmbeddingState<T>` resolves to the same string.
- `Type.FullName` → `Koan.Data.AI.EmbeddingState`​`` `1[[Demo.Todo, Demo, Version=…, Culture=…, PublicKeyToken=…]] `` — an assembly-qualified, version-pinned, bracket/backtick-laden string.

A repo-wide audit (per-adapter trace, adversarially verified) found this is **not** a single-adapter defect — it is a defect *in the shared chokepoint*, and its severity ranges from silent corruption to a hard crash:

| Adapter | Generic-entity behavior before this ADR |
|---|---|
| **Postgres / Redis / JSON** | type arg dropped → **all `EmbeddingState<T>` collapse into one physical store → silent cross-entity contamination** (a `Query<A>` deserializes B's rows) |
| **Couchbase** | mangled name violates the collection charset → **`CreateCollectionAsync` fails outright** |
| **SQLite / SQL Server / Mongo** | assembly-qualified mangle → illegal/clamped, **version-fragile** (re-versioning the entity assembly silently orphans the table) |
| **InMemory** | immune — keys by the `Type` object, never derives a name |

Crucially, the audit **confirmed the centralization already exists**: every persisted-name adapter routes `AdapterNaming.GetOrCompute` / `VectorAdapterNaming.GetOrCompute` → `factory.ResolveStorage` (the default `INamingProvider.ResolveStorage`; **no adapter overrides it**) → `StorageNameGenerator.Generate` → **`StorageNameResolver.Resolve`**. The question was never "centralize vs. push down" — it was "is the existing centralization universal?" It is. So one edit at the chokepoint fixes every adapter at once, and on relational adapters CREATE==QUERY is automatic (the schema orchestrator reflectively invokes the same `GetOrCompute`).

---

## Decision

### 1. The grammar

A storage name is composed from three orthogonal separators, each owning one concern:

| Separator | Concern | Example |
|---|---|---|
| `.` (the convention's `Separator`) | namespace path | `sample.todo` |
| `-` | **spine** — a single-arg wrapper / satellite around its subject | `todo-embeddingstate`, `todo-inner-wrap` |
| `--` | **branch** — sibling type arguments of a multi-arg generic | `a--b-link` |
| `#` / `_` / `-` (the adapter's `PartitionSeparator`) | partition | `todo-embeddingstate#backup` |

The entity is always the **leftmost anchor**; wrappers append to the right (inside-out), so a satellite sorts next to its entity at every nesting depth (`todo`, `todo-b`, `todo-b-a` all cluster under `todo*`).

### 2. The rule — one recursive line at the chokepoint

In `StorageNameResolver.Resolve` (`src/Koan.Data.Abstractions/Naming/StorageNameResolver.cs`), after the explicit `[Storage]` / `[StorageName]` checks and **before** the namespace style branches:

```
Resolve(G<a₁..aₙ>) = join("--", aᵢ => Resolve(aᵢ)) + "-" + ApplyCase(SimpleName(G), casing)
```

`SimpleName` strips the CLR arity marker (`EmbeddingState`​`` `1 `` → `EmbeddingState`). The recursion re-enters `Resolve` for each argument, so each argument gets the adapter's **full** style/casing treatment (namespace, hash, casing) — there is no per-style special-casing. For `N = 1` (every real satellite) the join is a no-op (`todo-embeddingstate`); nesting falls out of recursion (`A<B<Todo>>` → `todo-b-a`); the `--` branch only ever renders for a multi-arg generic.

Explicit `[Storage]` / `[StorageName]` on a generic definition still win and (intentionally) collapse all closures — the author's choice.

### 3. Casing — uniform per the adapter's announced convention

The wrapper segments are cased exactly like every other segment (`todo-embeddingstate`, not `todo-EmbeddingState`). Readability is carried by the `-` boundary; the convention's `Casing` is an adapter's **announced identifier law** (some stores — Elasticsearch/OpenSearch index names — are lowercase-only), and a default must never emit an identifier that is illegal on a backend the rule's scope could reach. Correct-everywhere outranks prettiest-in-isolation.

### 4. The contract: unique, not reverse-parseable

Names are **opaque unique keys** — the framework never reverse-parses a name (the `StorageNameGenerator.Clamp` comment is canon: "only uniqueness must be preserved, not recoverability"). `--` exists to *increase distinctness*: it makes `Link<A,B>` (`a--b-link`) ≠ `Link<B<A>>` (`a-b-link`) — the one **true collision** (two logical types, one physical name) the opaque-key contract does not tolerate. It does **not** make the string context-free-parseable, and it does not need to.

### 5. Conformance, re-points, and gaps closed

- **All adapters route through the chokepoint** (audit-confirmed; no `ResolveStorage` overrides). The one edit is sufficient.
- **Two bypasses re-pointed**: Redis (`RedisRepository.Keyspace`) and JSON (`JsonRepository.ComputePhysicalName`) returned `typeof(TEntity).Name` raw on the `AppHost.Current == null` fallback; both now route through `StorageNameResolver.Resolve`.
- **Couchbase capability gap closed**: `CouchbaseAdapterFactory` now announces `MaxIdentifierBytes = 251` (collection cap) so over-long names clamp rather than producing an invalid collection identifier.
- **Dormant divergence noted**: `RelationalModelBuilder.ResolveDefaultName` hardcodes a capability-ignoring convention, but `RelationalModelBuilder` has **zero callers** (dead) and its `ResolveDefaultName` delegates to `StorageNameResolver.Resolve`, so the generic fix auto-covers it. The style-hardcoding is a separate, dormant cleanup.

### 6. Scope and carve-outs

**In scope**: data storage names (the `StorageNameResolver` path). Vector adapters ride the same resolver and inherit the fix.

**Explicitly carved out** — other `Type → identifier` sites the audit found, which the chokepoint fix does **not** reach (each is a separate path):

- **`JobRecord.WorkType`** (`JobTypeBinding`, persisted ledger key) and the **messaging routing key** (`MessagingExtensions.GetConcreteTypeName`, wire identifier) also mangle closed generics — but they are **persisted / on-the-wire**, so changing their derivation orphans existing ledger rows / breaks in-flight messages. **Breaking change → separate card with a compat path. Not touched here.**
- **Cache keys** (`CacheKey.For`, `EntityCacheExtensions`, `CachedRepository`) mangle too but are **ephemeral / re-derivable** — lower-priority follow-up card.
- **Raw-SQL entity-token rewrite** (`RewriteEntityToken`) silently no-ops for generics (`\b` won't match a backtick) — raw-SQL-only, noted.

---

## Consequences

- **One core edit fixes nine adapters**; the recursion needs no per-adapter or per-style code. Silent cross-entity corruption (Postgres/Redis/JSON), the Couchbase create-failure, and the version-fragile relational names are all resolved at once.
- **Guard (binding)**: a resolver grammar matrix (single / nested / branch / attribute-honoring / casing / legality / partition-composition across all three styles), a capability-honesty oracle over the real in-proc factories, and an **ARCH-0079** real-`AddKoan()` relational round-trip proving a generic entity creates + round-trips on SQLite with distinct closures in distinct tables. The container-backed cross-adapter oracle extends this per surface suite.
- **Backward-compat is a non-issue**: relational never created these tables (it threw), and the sole-consumer/dogfood posture means orphaned schemaless `Foo`​`` `1 `` collections (which had already collided every closure into one) are not worth migrating.
- **The grammar is canon**: `.` namespace · `-` spine · `--` branch · `#`/`_`/`-` partition, leftmost-anchor, arity-stripped, uniformly cased.
