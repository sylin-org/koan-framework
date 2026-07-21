# DATA-0102: Couchbase — native CAS (ConditionalReplace); FastRemove declined

**Status**: Accepted (2026-06-17)
**Date**: 2026-06-17
**Deciders**: Enterprise Architect
**Scope**: Resolves assessment card **E8b** (Track E). The Couchbase data connector implements `IConditionalWriteRepository` (atomic compare-and-set) using Couchbase's native CAS and declares `DataCaps.Write.ConditionalReplace`. It **declines** `DataCaps.Write.FastRemove`, with the reasoning recorded here.
**Related**: **JOBS-0005 §20.3** (CAS / `IConditionalWriteRepository` for contention-free claiming) · **ARCH-0084** (unified capability model — caps are honest self-report) · **DATA-0083** (fast-remove strategy) · the Mongo CAS reference (`MongoRepository.ConditionalReplaceAsync`).

---

## Context

Cartography flagged that Couchbase "missed the CAS / FastRemove / TTL waves." Re-derivation of `CouchbaseRepository.cs`:

- It declares `Query.String/Linq`, `Write.BulkUpsert/BulkDelete/AtomicBatch`, `Query.Filter` — but **not** `Write.ConditionalReplace` or `Write.FastRemove`, and does not implement `IConditionalWriteRepository`.
- It **has native CAS** (every document carries a CAS value; `ReplaceOptions.Cas` is optimistic concurrency) and real multi-document transactions (used by the atomic batch path).
- `RemoveAll(strategy)` already issues a **server-side N1QL `DELETE FROM <keyspace>`** for every strategy — not a per-document client-side loop. Its own comment notes no admin-free fast path exists.

The CAS contract (`IConditionalWriteRepository<TEntity,TKey>`): `Task<bool> ConditionalReplaceAsync(TEntity model, Expression<Func<TEntity,bool>> guard, ct)` — apply the replace iff the guard holds on the document's pre-update state; return `true` if applied, `false` if the guard no longer held (another writer won the race). Mongo implements it as a single atomic `ReplaceOne` whose filter is `_id == model.Id AND <lowered guard>`.

---

## Decision

### 1. Implement CAS (`ConditionalReplace`) — native, no transaction needed

`CouchbaseRepository` implements `IConditionalWriteRepository<TEntity,TKey>` and declares `DataCaps.Write.ConditionalReplace`. The implementation uses Couchbase's native CAS:

1. `GetAsync(key)` — capture the document's current content **and** CAS.
2. Evaluate the compiled `guard` against the current content. If it does not hold (or the document is absent), return `false` — the claim is already lost; nothing is written.
3. `ReplaceAsync(key, model, ReplaceOptions.Cas(currentCas))` — the CAS guard makes the write atomic: if another writer mutated the document between step 1 and step 3, the SDK throws `CasMismatchException`, which we map to `false`.
4. `DocumentNotFoundException` → `false`.

This is the idiomatic Couchbase optimistic-concurrency pattern and is contract-compliant: a `CasMismatchException` is exactly "another writer won the race." It enables JOBS-0005 §20.3 **contention-free claiming** on Couchbase, at parity with Mongo / Postgres / SqlServer / SQLite.

**Semantic note.** Couchbase CAS is *version*-based: any concurrent change between the read and the CAS-guarded replace fails the write (returns `false`), even if the new state would also satisfy the guard. Mongo re-evaluates the guard atomically in the single `ReplaceOne`. The Couchbase form is *stricter*, which is correct and safe for optimistic-concurrency claiming (the caller retries) — it never applies a replace that would violate the guard.

### 2. Decline FastRemove — no path beats the existing server-side DELETE

Couchbase **does not** declare `DataCaps.Write.FastRemove`. `RemoveAll` continues to serve all strategies with its server-side N1QL `DELETE`. The "fast" (TRUNCATE/DROP) analog for Couchbase would be drop-and-recreate the collection (mirroring Mongo's fast path), but for Couchbase that path:

- pays a **multi-second `CREATE PRIMARY INDEX` recreation** (the adapter's `EnsurePrimaryIndex` waits up to ~15s for the index to report `online`) — making it **slower than the N1QL `DELETE`** in the common case;
- leaves the collection **briefly unqueryable** (no primary index) during recreation — an availability regression;
- the only other option, `FlushBucket`, requires **admin** privileges and is bucket-coarse.

So Couchbase has no fast-remove path that is strictly better than what it already does. Declaring `FastRemove` would be a dishonest capability (ARCH-0084: caps are honest self-report). `RemoveStrategy.Optimized` therefore resolves to the server-side N1QL `DELETE`, which is the right behavior.

---

## Consequences

**Positive**
- Couchbase gains atomic CAS → JOBS-0005 distributed/contention-free claiming works on Couchbase, closing the parity gap with the other durable adapters.
- The capability surface stays honest: `ConditionalReplace` declared (and implemented), `FastRemove` correctly absent.

**Cost / caveats**
- CAS does one extra `GetAsync` per conditional replace (read-then-CAS-replace) — inherent to KV optimistic concurrency; the alternative (N1QL `UPDATE ... WHERE guard`) cannot cleanly replace a whole document.
- `RemoveAll` performance is unchanged (server-side N1QL `DELETE`); large-collection clears are bounded by that DELETE, not a drop.
