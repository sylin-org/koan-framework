---
type: REF
domain: framework
title: "Glossary"
audience: [developers, architects, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: verified
  scope: docs/reference/glossary.md
---

# Glossary

The vocabulary you meet across Koan's docs and source, each pinned to the type that
defines it. When a term and its code drift, the code wins — every entry below links the
defining type so you can confirm the contract for yourself.

## Core model

**Entity** — A domain object that *is* your data-access surface. You inherit
`Entity<TEntity>` (string id, GUID v7 generated on first read) or `Entity<TEntity, TKey>`
for a custom key, and you get static conveniences (`Get` / `All` / `Query`) and instance
verbs (`Save` / `Remove`) without writing a repository. The entity, not a service layer,
is the unit of persistence. Defined by
[`Entity<TEntity, TKey>` / `Entity<TEntity>`](../../src/Koan.Data.Core/Model/Entity.cs).

**Environment snapshot** — The immutable process/runtime snapshot Koan exposes statically so code can
read environment, container/CI/orchestration mode, session id, and application identity without
threading configuration through call sites. It is configuration, not logical-flow context. Defined by
[`KoanEnv`](../../src/Koan.Core/KoanEnv.cs).

**Logical-flow context** — Exact-type, immutable-snapshot state attached to the current async execution
flow. Module-owned values flow through `await`; nested scopes restore their predecessor; host services
and disposable resources do not belong here. Defined by
[`KoanContext`](../../src/Koan.Core/Context/KoanContext.cs).

**Context carrier** — A module-owned, versioned serializer for one logical-flow axis. The Core registry
captures opaque values for durable work, restores or suppresses them as a scope, and refuses unknown
axes or insufficient ingress trust before user code. Defined by
[`IKoanContextCarrier`](../../src/Koan.Core/Context/IKoanContextCarrier.cs) and
[`KoanContextCarrierRegistry`](../../src/Koan.Core/Context/KoanContextCarrierRegistry.cs).

**Ingress trust** — Provenance claimed by the mechanism delivering a carried context bag:
`Unverified`, `Authenticated`, or `HostTrusted`. It says nothing about authorization,
confidentiality, delivery, or payload correctness. Defined by
[`ContextIngressTrust`](../../src/Koan.Core/Context/ContextIngressTrust.cs).

## Data routing

**Source** — A *named* configuration profile (e.g. `"analytics"`, `"backup"`) that selects
which backing store an operation runs against; a source defines its own adapter, so it is
mutually exclusive with an explicit adapter override. You scope to one with
`EntityContext.With(source: …)`. Defined by the routing context in
[`EntityContext`](../../src/Koan.Data.Core/EntityContext.cs).

**Partition** — A storage-suffix that splits one entity's data into logically distinct
physical stores (e.g. `"archive"`, `"cold"`, a tenant id) while keeping the same entity
type. Partition names are validated at `EntityContext.With(partition: …)` so two
distinct names can never collapse onto one store after identifier sanitization. Defined by
the routing context in [`EntityContext`](../../src/Koan.Data.Core/EntityContext.cs) and
enforced by
[`PartitionNameValidator`](../../src/Koan.Data.Core/PartitionNameValidator.cs).

**Set** — The historical name for what is now called a **partition** — a logical
data subset selected at routing time. The current canonical concept and routing parameter
is `partition`; treat any "set" you see in older docs or samples as "partition". Defined by
the routing context in [`EntityContext`](../../src/Koan.Data.Core/EntityContext.cs)
(see the `partition` parameter) and
[`PartitionNameValidator`](../../src/Koan.Data.Core/PartitionNameValidator.cs).

## Storage providers

**Adapter** — A provider's implementation of the data contract: the write/CRUD surface
(`Get` / `Upsert` / `Delete` / batch verbs) that every backing store implements, with
querying and counting on the sibling query contract. An adapter is "a translator and an
executor"; the framework, not the adapter, owns fallback logic. Defined by
[`IDataRepository<TEntity, TKey>`](../../src/Koan.Data.Abstractions/IDataRepository.cs).

**Connector** — A referenceable package (e.g. `Koan.Data.Connector.Postgres`) that ships an
adapter plus its storage-naming and repository-creation factory. Referencing a connector is
what makes its provider available for election (see *Reference = Intent*). The contract a
connector implements is the adapter factory. Defined by
[`IDataAdapterFactory`](../../src/Koan.Data.Abstractions/IDataAdapterFactory.cs).

## Query negotiation

**Capability / CapabilitySet** — The single negotiation surface for "what can this provider
do". A provider declares its support once into a `CapabilitySet` (one type that replaced
~40 ad-hoc capability enums and marker interfaces); callers then check it with `Has` /
`Require` against named capability tokens such as `DataCaps.Query.Linq`. Defined by
[`CapabilitySet`](../../src/Koan.Core/Capabilities/CapabilitySet.cs), with the data tokens
in [`DataCaps`](../../src/Koan.Data.Abstractions/Capabilities/DataCaps.cs).

**Pushdown** — Executing the part of a query the adapter natively supports *in the store*
(translated to SQL/N1QL/etc.) rather than in process. The coordinator splits a caller's
filter against the adapter's declared support and hands the adapter only the pushable
portion. Defined and orchestrated by
[`FilterPushdownCoordinator`](../../src/Koan.Data.Core/Querying/FilterPushdownCoordinator.cs).

**Residual** — The remainder of a query that the adapter could *not* push down, evaluated
in process after the store returns rows. Pagination is applied after the residual filter —
structurally eliminating the "paginate the unfiltered set, then filter" bug — and this
lives in exactly one place so no adapter carries fallback logic. Defined alongside pushdown
in
[`FilterPushdownCoordinator`](../../src/Koan.Data.Core/Querying/FilterPushdownCoordinator.cs).

## Bootstrap and provenance

**Registrar / KoanModule** — The boot-time module primitive: one self-describing unit an
assembly author writes to register services (`Register`), declare ordered one-time startup
(`Start`), and self-report (`Report`). `KoanModule` implements the registrar interface, so
the source-generated discovery and `[Before]`/`[After]` ordering apply to it unchanged — it
is the preferred way to author what older code wrote as a hand-rolled `KoanAutoRegistrar`.
Defined by [`KoanModule`](../../src/Koan.Core/KoanModule.cs), over the
[`IKoanAutoRegistrar`](../../src/Koan.Core/IKoanAutoRegistrar.cs) contract.

**Reference = Intent** — The principle that adding a package reference *is* the
configuration: each referenced module registers itself through discovery, so `Program.cs`
stays at four lines (`AddKoan()`) regardless of how many capabilities are wired. The
mechanism is the auto-registrar discovered at boot. Defined by
[`IKoanAutoRegistrar`](../../src/Koan.Core/IKoanAutoRegistrar.cs); the principle is written
up in [architecture/principles.md](../architecture/principles.md#reference--intent).

**Provenance** — The structured record of *who configured what and from where*: every module
publishes settings, tools, and notes (with their source — app settings, environment,
defaults) into a registry that holds the current snapshot. Provenance is the data behind the
boot report. Defined by
[`ProvenanceRegistry`](../../src/Koan.Core/Provenance/ProvenanceRegistry.cs)
(contract: [`IProvenanceRegistry`](../../src/Koan.Core/Provenance/IProvenanceRegistry.cs)).

**Boot report** — The human-readable block Koan prints to the console at startup, rendering
the provenance snapshot: every discovered module, the elected adapters, configuration
sources, and boot phases. It is the framework's primary debugging surface — most "why isn't
X registered" questions are answered by reading it. Rendered by
[`KoanConsoleBlocks`](../../src/Koan.Core/Logging/KoanConsoleBlocks.cs) from the provenance
snapshot.

## Background jobs

**Lane** — A named concurrency pool for job actions. Each job action runs in a lane
(defaulting to the action name, so each action is its own pool), and a lane's
`MaxConcurrency` caps how many of its work-items run at once; the dispatcher skips a job
whose lane is saturated. Defined by `JobActionAttribute.Lane` in
[`JobAttributes`](../../src/Koan.Jobs/JobAttributes.cs).

**Gate (jobs)** — A shared-resource lock a job declares so the orchestrator can check
contention *at dispatch, without running the handler*. The attribute names a work-item
property whose value becomes the gate key; only one job per gate key runs at a time. Defined
by `JobGateAttribute` in [`JobAttributes`](../../src/Koan.Jobs/JobAttributes.cs).

**Ledger** — The single source of truth and single writer for jobs: the ledger *is* the
queue. Dispatch claims the next ready row by atomic compare-and-set (claim, never a move);
there is no separate volatile queue to reconcile, and the physical layout (in-memory,
data-backed, hot/cold) hides behind the interface. Defined by
[`IJobLedger`](../../src/Koan.Jobs/IJobLedger.cs).

## Cache

**Coherence** — The mechanism that keeps L1 caches consistent across nodes: when one node
invalidates an entry, it broadcasts a `CacheInvalidation` over a coherence channel and other
nodes apply it to their local L1. Adapters (Redis pub/sub, the messaging bus, in-memory for
tests) implement the cache-specific channel. Defined by
[`ICacheCoherenceChannel`](../../src/Koan.Cache.Abstractions/Coherence/ICacheCoherenceChannel.cs).

**Fresh-or-null** — The default cache read contract: a read past the entry's absolute TTL
returns `null` (a cache miss), never stale data. Stale-while-revalidate is an explicit
per-call opt-in via an `AllowStaleFor` window — there is no global toggle, so staleness is
never a surprise. Defined by `CacheReadOptions.Default` in
[`CacheReadOptions`](../../src/Koan.Cache.Abstractions/Primitives/CacheReadOptions.cs).
