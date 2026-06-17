# DATA-0101: Redis-as-data — keep `FilterSupport.Full`, add native TTL

**Status**: Accepted (2026-06-17)
**Date**: 2026-06-17
**Deciders**: Enterprise Architect
**Scope**: Resolves the assessment item E8c ("Redis stops advertising `FilterSupport.Full` for scan-all, **or** gains cursor paging + native TTL, **or** is demoted to cache/coherence duty only" — `docs/assessment/04-recommendations.md` §6). Decides that the Redis **data** connector (`Koan.Data.Connector.Redis`) **keeps** `FilterSupport.Full`, **gains native TTL** via `[Index(Ttl = true)]` → `EXPIREAT` (`DataCaps.Retention.TtlIndex`), and treats the full-keyspace scan as a **documented cost**, not a capability lie. Declines the "retract Full", "secondary-index pushdown", and "demote" alternatives.
**Related**: **ARCH-0084** (unified capability model — `FilterSupport` is operator-level, not pushdown-efficiency) · **DATA-0100** (states the same: "why `FilterSupport` stays operator-level") · **DATA-0096** (unified filter pipeline — the `FilterPushdownCoordinator` residual is the entity-path in-memory floor) · **DATA-0081** (the in-memory adapter — the reference "Full floor" sibling) · **JOBS-0005 §20.4** (origin of `[Index(Ttl)]` / `DataCaps.Retention.TtlIndex`, as implemented by Mongo).

---

## Context

The assessment flagged Redis-as-data as the data tier's one honesty gap: it advertises `FilterSupport.Full` while backing every filter with a full-keyspace SCAN + client-side filter ("risky at scale" — `docs/SURFACES.md`, `01-cartography.md`, `04-recommendations.md`). The card offered three remedies: retract `Full`, make `Full` real (cursor paging + indexes + TTL), or demote Redis to cache/coherence-only.

Empirical re-derivation (read of `RedisRepository.cs`, `FilterSupport.cs`, `FilterSplitter.cs`, `FilterPushdownCoordinator.cs`, `Data.cs`, `InMemoryRepository.cs`, `MongoRepository.cs`) confirms the **mechanics** the card describes — but corrects its **framing**:

1. **`Full` is the prescribed token for an in-memory-evaluating adapter, not a claim of efficient pushdown.** `FilterSupport` is defined as "the operator set a provider can push down… an operator the provider cannot **honour faithfully** must be left out so negotiation fails loud" (`FilterSupport.cs`). `Full` is documented as "*e.g. in-memory / oracle adapters that evaluate the AST directly*". Redis materializes the keyspace and evaluates the entire AST via `InMemoryFilterEvaluator` — it honours **every** operator faithfully. So `Full` is **correct**.
2. **The reference adapter declares `Full` identically.** `InMemoryRepository` (DATA-0081) is documented as a "Full floor adapter… declares `FilterSupport.Full` and evaluates the entire Filter via `InMemoryFilterEvaluator` — so the coordinator never produces a residual for it." Redis is the same shape. Making Redis declare `None` would make it the **lone** in-memory adapter that disagrees with the model.
3. **On the entity path a residual is not an error — it is an in-memory floor.** `Data.QueryWithCount` runs `Plan → adapter → Finalize`; `Finalize` applies any residual via `InMemoryFilterEvaluator`. The "any non-empty filter becomes a residual and therefore a hard error" warning on `FilterSupport.None` is the **vector** path (`VectorFilterCoordinator`, which has no in-memory floor), not the entity path. So declaring `None` on Redis would **not** error — it would route the *identical* in-memory filtering from the adapter into the coordinator. Same SCAN, same cost, same result — only the self-report would change, and it would change to something the model says is wrong.
4. **The real concern is cost, which `FilterSupport` deliberately does not model.** The full-keyspace SCAN is a genuine performance cliff, but it is not expressible as a filter-operator capability. Encoding it would require a *new* capability axis — out of scope for E8c, and contradicting ARCH-0084's deliberately operator-level design (see DATA-0100, Force 2).
5. **The genuine, closable gap is native TTL.** Mongo declares `DataCaps.Retention.TtlIndex` and expires rows via a single-property `[Index(Ttl = true)]` timestamp (`expireAfterSeconds = 0`). Redis — whose key expiry is a native, signature feature — declares neither and writes keys with no expiry. This is a real parity gap and Redis's actual strength.

### Forces

1. **Correctness over connotation.** `FilterSupport.Full` means "I return correct results for every operator," which Redis does. Renaming it to `None` to signal "this is slow" would trade a true statement for a false one.
2. **Consistency.** InMemory (and Json) declare `Full` for the same in-memory-evaluation reason. Redis must agree, or the capability model loses its single meaning.
3. **Fewer, more meaningful parts.** Redis-as-data with native TTL is a *differentiated* part — the ephemeral keyed store with O(1) get/set and store-native expiry — not a duplicate of the relational/document adapters. That earns its place; a pretend-query-DB would not.
4. **Cost belongs where it can be told truthfully.** The SCAN cliff is documented (adapter XML, the surface ledger), not mis-encoded as a missing filter operator.

---

## Decision

1. **Keep `FilterSupport.Full`.** It is the correct token for an in-memory-evaluating adapter (ARCH-0084). The full-keyspace SCAN backing reads is a documented cost, recorded in the adapter XML and the surface ledger — not a capability lie. No code change to the filter declaration.

2. **Add native TTL.** `RedisRepository` declares `DataCaps.Retention.TtlIndex` and honours a single-property `[Index(Ttl = true)]` timestamp: on every write (`Upsert` / `UpsertMany`) the key's expiry is set to that property's instant via `KeyExpire`/`EXPIREAT`.
   - **Null/absent value → key persists** (no expiry) — mirrors Mongo "a null/absent value is never expired."
   - **Past instant → key removed** (native `EXPIREAT` semantics) — mirrors Mongo `expireAfterSeconds = 0`.
   - The TTL property is resolved **once per closed entity type** (via the shared `IndexMetadata.GetIndexes`); entities without a TTL index pay **zero** hot-path cost and keep the single-round-trip `SET`/`MSET` write path.

3. **Decline the alternatives.**
   - *Retract `Full` → `None`*: rejected — `Full` is correct (Forces 1–2); `None` would be behaviorally identical, inconsistent with InMemory/Json, and contradict the model.
   - *Make `Full` real (secondary indexes / RediSearch + cursor paging)*: rejected — disproportionate; Redis is not a query database, and this duplicates capability the relational/document adapters already provide well.
   - *Demote to cache/coherence-only*: rejected — it forecloses the native-TTL niche that makes Redis-as-data a meaningful part. `Koan.Cache.Adapter.Redis` (L2 + coherence) is unaffected and remains the separate cache surface.

---

## Consequences

**Positive**
- The capability self-report (`DataCaps.Describe`, `/.well-known/Koan/aggregates`) stays **correct and consistent** with the other in-memory-floor adapters.
- Redis gains **native TTL** — parity with Mongo and Redis's signature feature — so the assessment "gap" is closed by *correct understanding* plus a *real feature*, not by a behaviorally-null, model-contradicting rename.

**Cost / caveats**
- Reads remain **full-keyspace scans** (`Query` / `Count` / `DeleteAll` materialize via `ScanAll`). This is unchanged and now explicitly documented as the adapter's cost characteristic. Redis-as-data is for keyed, ephemeral, or modest-cardinality entities — not large filtered result sets.
- TTL adds **one `EXPIREAT` round-trip per TTL-annotated write**; non-TTL entities are unaffected (the TTL property resolves to `null` once per type).

**Follow-up (out of scope)**
- A general **cost / scan-backed signal** in the capability model — letting callers detect the SCAN cliff programmatically — is a real future want, but it is a capability-model addition (ARCH-0084 surface) and belongs to its own ADR/card, not E8c.
