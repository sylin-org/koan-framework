---
type: SPEC
domain: data
title: "DAC-46 Rebuild and Certify the Redis Adapter"
audience: [architects, maintainers, developers, ai-agents]
status: connector-acceptance-green
last_updated: 2026-07-28
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-28
  status: connector-acceptance-green
  scope: Redis greenfield implementation, real-provider acceptance, source-operation regression, and shared backend regression
---

# DAC-46 — Rebuild and certify the Redis adapter

| Field | Value |
|---|---|
| Phase / kind | fleet / whole-adapter greenfield rebuild |
| Depends on | DAC-40 |
| Primer scope | dynamically selected narrow Redis manifest |
| Production writes | Redis connector, focused Redis tests/docs, one provider-neutral Function binding, and bounded shared Redis connection ownership authorized |
| Owner | Adapter(Redis); Source Integration binding leaf; Redis backend resource owner |

## Meaningful outcome

Redis demonstrates that a deliberately narrow key/value provider can be delightful and fully conformant without fake
relational/document parity, whole-server keyspace scans, or overstated paging, lifecycle, durability, and atomicity.

## Approved greenfield exploration

**Task:** Replace the Redis Data connector from an empty implementation root and certify it against the current Koan
Data contract and pinned Redis 8.8 provider.

**Application intent:** Reference Redis, call `AddKoan()`, and use ordinary `Entity<T>` keyed persistence with native
TTL. A managed source may query a deliberately bounded Koan-owned logical set. An external source may read and mutate
known keys according to source access policy, or execute an explicitly registered read-only Redis Function without
granting Koan ownership of the surrounding database.

**Public expression:** Managed persistence remains package + `AddKoan()` + `Entity<T>`. Existing physical JSON values
may use `Source(...).Map<T>(map => map.Container(...).Key(...).Name(...).Property(...).Name(...).Object(...))`.
Named reads use the provider-neutral operation grammar and the compact native leaf `.Function(...)`. Source lifecycle,
access, database, limits, mappings, and read lanes remain application decisions; Redis keys, scripts, transactions,
and multiplexer objects do not enter ordinary Entity code.

**Guarantee/correction:** Known-key operations use Redis string commands directly. Managed logical sets maintain one
adapter-owned membership registry sharing the records' cluster hash slot; set operations traverse only that registry
and reject before work when its configured bound is exceeded. External sources create no registry or metadata and
therefore reject set enumeration/query/count/clear correctively. No Entity path calls `KEYS` or `SCAN`. Native TTL is
attached to each record. Single-record conditional replacement uses provider optimistic compare-and-set. ReadOnly
rejects before provider mutation. Registered Functions execute only through `FCALL_RO`, require a declared read lane,
and never load or manage server code. Unsupported streaming, generated identities, provider-bounded paging, atomic
batch, inspection, or structural lifecycle guarantees reject instead of being emulated or inferred.

**Complete intent surface:** Package reference, `AddKoan()`, reachable Redis, optional source policy/database, optional
compact map, optional TTL, and optional registered read-only Function are the complete user actions. Redis Function
deployment and external key/schema ownership remain infrastructure responsibilities.

**Public concepts:** Source selects endpoint/database/policy; Container names one logical key set; Partition selects a
Koan logical subdivision; Key/Property/Object/Name/Path define JSON and identity projection; lifecycle says whether
Koan owns the membership registry; access gates effects; Lane selects a provider-enforced read endpoint; Query/Scalar
select result shape; Function selects the native registered-read leaf. Redis exposes no honest portable Namespace,
Container inspection, or schema subdivision, so `Inspect()` declines.

**Docs read:** The Data Adapter Development Primer, architecture principles, DATA-0107, DATA-0110, the initiative
charter/acceptance/roadmap/current handoff, and connector README/TECHNICAL. They require compact application intent,
empty-root replacement, immutable warm plans, bounded resources/work, one owner, truthful decline, and real-provider
proof. Official Redis documentation establishes cursor-scan mutation caveats, native TTL, optimistic transactions,
read-only Function execution, and explicit Function key declarations. StackExchange.Redis documentation establishes
that one shared thread-safe `ConnectionMultiplexer` should be reused and that transaction conditions provide WATCH.

**Code read:** Public repository/query/capability contracts, source plans and policies, mapping plans, operation plans,
neutral records, health, naming, managed-field serialization, the host-owned Redis connection provider, and current
black-box Redis tests. The rebuilt Couchbase factory/resource shape is evidence for host ownership and source routing,
not a Redis implementation template. The retired Redis repository/factory/module/options/health code was read only to
harvest provider facts, public identities, and failure modes; its KeyValueStore inheritance, key scanning, global host
lookup, routing, claims, and control flow are not authoring inputs.

**Existing constants/options/contracts:** Provider identity `redis`, priority 5, configuration section
`Koan:Data:Redis`, source `Database`, Redis 8.8.0 fixture, StackExchange.Redis 3.0.17, `IRedisConnectionProvider`, source
policies/plans, mapping plans, Entity JSON/managed fields, filter AST/receipts, neutral operation results, capability
contracts, health/discovery, naming, and TTL contracts already exist.

**Reusing:** Ratified package/provider/configuration identities; the shared host-owned Redis backend; Data policy,
routing, mapping, serialization, managed-field, receipt, health, and source-operation contracts; StackExchange.Redis;
and the pinned real-provider fixture. The shared `KeyValueStore` remains for its existing InMemory/JSON consumers but
is not a Redis authoring substrate because its full-store enumeration contract conflicts with Redis boundedness.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| constants, typed options, immutable route, activation, health | `src/Connectors/Data/Redis` | one concrete provider authority and one compiled route |
| immutable JSON/key projection | `src/Connectors/Data/Redis/Runtime` | compile mapping and key encoding once per repository |
| direct-command repository and managed registry | `src/Connectors/Data/Redis/Runtime` | O(1) keyed hot path plus bounded set semantics without whole-server scans |
| source integration and neutral RESP reader | `src/Connectors/Data/Redis/Runtime` | provider-enforced bounded named reads without Entity manufacture |
| `FunctionOperationBinding` and `.Function(...)` | Data Abstractions/Core operation folders | one provider-neutral native leaf already ratified by DATA-0110 |
| bounded endpoint cache in shared Redis provider | `src/Koan.Redis/Connections` | prevent unbounded host-lifetime multiplexer growth while preserving shared ownership |
| focused real-provider cases | Redis connector test suite | prove keyed CRUD, managed bounds, external decline, TTL, CAS, mapping, functions, routes, and claims |

**Coalescence:** Disposition is `REBUILD`: remove every retired Redis connector implementation file before authoring
the replacement. One factory compiles route + mapping; one repository owns native persistence; one projection owns
wire JSON/key mechanics; one source integration owns `FCALL_RO`; the existing backend remains the sole multiplexer
owner. A Redis-specific Family would add a second semantic owner with no second provider consumer. A repository-owned
multiplexer would reconnect per entity. Reusing `KeyValueStore` would retain hidden global scan semantics. A second
metadata catalog beyond the membership set has no contract and is rejected.

**Ergonomics:** Ordinary code remains Entity verbs. `.Container`, `.Name`, `.Object`, `.Path`, and `.Function` read the
same across providers. Queries over managed small sets work without Redis ceremony and explain their configured bound;
large or externally owned sets fail with a corrective message before whole-keyspace work. Native Functions provide a
low-cost "show me what is there" integration path without inventing relational tables or document collections.

**Constraints satisfied:** No Entity stream claim, global key scan, hidden client paging, generated key, synthetic
schema, application-callback replay, process-global service lookup, unbounded resource cache, secret diagnostic,
sync-over-async bridge, or old/new compatibility path. Typed limits are positive and immutable in each route. Warm
operations consume compiled key/mapping/filter plans. README/TECHNICAL and claims change with executable behavior.

**Risks:** Expired records can leave stale managed membership until bounded read/write cleanup; Redis transactions do
not roll back runtime command errors; cluster-safe multi-key work requires shared hash tags; logical databases are not
available in Redis Cluster; eviction can remove durable-looking data; arbitrary Function result shapes need a strict
neutral algebra; ACL read-only posture must allow the exact commands used by `FCALL_RO`; and a membership bound is a
correctness boundary, not a performance suggestion.

## Harvested baseline

The retired connector passed 12/13 cases against `redis:8.8.0-alpine` in a five-second test run. Its managed-field
isolation case failed because the expected registration did not exist in the composed host. More importantly, source
inspection and review showed that every set operation enumerated Redis server keys and then evaluated filters in
memory, route naming used process-global host state, capability declarations contradicted implemented bulk behavior,
connection routes accumulated without a bound, and External sources had no ownership-safe set boundary. These are
frozen negative lessons and black-box scenarios, not implementation inputs.

## Replacement result

The retired Redis Data connector implementation was removed before the replacement was authored. The current adapter
has one factory, immutable route/entity/key plans, one direct-command repository, a Koan-owned bounded membership set
for Managed containers, a strict keyed-only External posture, and one `FCALL_RO` source integration. It does not
inherit `KeyValueStore`, resolve services through `AppHost.Current`, call `KEYS`/`SCAN`, maintain an unbounded adapter
pool, or retain an old/new compatibility path. The common Redis backend now bounds distinct host-owned endpoint
multiplexers at 128 and fails correctively at the ceiling.

Real-provider acceptance against `redis:8.8.0-alpine` and StackExchange.Redis 3.0.17 is green at 17/17 with zero skips.
The cases prove keyed CRUD/get-many, three isolation modes, polymorphism, TTL, compact external JSON mapping and unknown
field preservation, ReadOnly rejection, managed query bounds, conditional compare-and-set, source/database routing,
shared connection ownership, read-only Functions returning neutral records/scalars, honest inspection decline, and
stream rejection. Bulk upsert/delete use one Redis transaction on the ordinary unmapped hot path; record keys and their
membership set share one cluster hash slot.

The provider-neutral Source Integration regression is green at 24/24. The shared Redis Cache suite is green at 6/6
after bounding backend endpoints. Redis Web AdapterSurface passes 43/52; nine transfer-only cells skip explicitly
because cross-partition transfer requires provider-bounded Entity streaming and Redis correctly declines that claim.
No failed Web cell remains. Connector, test, Web, Cache, Core Source Integration, and backend builds complete with zero
warnings. Heavy ACL, restart/fault, soak, Forge, and packet validation remain fleet-certification evidence rather than
connector-acceptance claims.

## Write boundary

Allowed: `src/Connectors/Data/Redis/**`, focused Redis Data tests, Redis connector docs, the source-operation Function
binding/extension and their focused Core tests, `src/Koan.Redis/Connections/RedisConnectionProvider.cs`, and this
initiative's DAC-46/evidence/ledger handoff files. Forbidden: other adapters, the KeyValue family implementation,
unrelated Framework semantics, cache behavior, product claims, commits, pushes, and external environment mutation.

## Verification

- Build Redis connector, shared source-operation owners, and Redis tests with zero warnings.
- Execute the complete Redis Data suite against pinned Redis 8.8.0 with no skipped LIVE cases.
- Run focused Source Integration regression for the Function binding and shared Redis backend/cache regressions when
  the connection-owner bound changes.
- Reconcile executable claims, README/TECHNICAL, and the DAC-46 result; record heavier ACL/restart/soak/Forge evidence
  separately when it is not part of connector acceptance.

## Definition of done

- [x] Retired Redis connector implementation is absent and one new execution path remains.
- [x] Redis connector acceptance is green for a narrow truthful profile.
- [x] Keyed access, TTL, CAS, mapping, managed bounds, External/ReadOnly, routes, Functions, and resource ownership have
      exact native evidence.
- [x] Unsupported stream/paging/inspection/lifecycle/atomic paths reject correctively.
- [x] Claims, docs, tests, and runtime behavior agree for connector acceptance.
- [ ] Fleet ACL, restart/fault, soak, Forge, and packet-validator evidence is complete.

## Stop conditions

An unbounded scan, provider operation that can bypass source access, ambiguous Function result algebra, unbounded
multiplexer ownership, unavailable real Redis, or a required change to a second unlisted semantic owner stops work.
