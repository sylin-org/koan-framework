---
type: REFERENCE
domain: core
title: "Solution compositions"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/solutions.md
---

# Solution compositions

Use this page when one recipe is not the whole application. A solution receipt is the handoff between
capability discovery and implementation: it unions the references and Entity shapes, carries every
selected capability's constraint, and names proof that crosses the seams.

## What a complete receipt contains

| Field | What the agent must settle |
|---|---|
| Package union | every exact package reference needed by the chosen path; no guessed identifiers |
| Entity vocabulary | ordinary Entities, specialized bases, attributes, terminals, controllers, and contexts |
| Runtime dependencies | files, models, services, credentials, and deployment topology outside NuGet |
| Inherited constraints | the constraint box from every selected capability, including interactions such as tenancy across Jobs |
| Proof | behavior, actual composition/provider participation, and a useful correction path |

The receipts below are starting compositions, not universal architectures. Open every linked recipe
before writing unfamiliar code; it owns configuration, working syntax, and provider limits.

## Standing constraints

- A solution receipt unions the constraint boxes of every selected capability - and interactions
  count (tenancy across Jobs, access rules under MCP, auth before tenancy).
- The receipt is the plan the proof must catch; it is not an architecture that survived execution.

## Do not, at this level

- Do not compose from capability names alone - every package id and Entity shape below is
  verified against the linked recipes, and yours must be too.

## Approval desk: one Entity, HTTP, and an outside agent

| Receipt field | Composition |
|---|---|
| Package union | `Sylin.Koan.App` · `Sylin.Koan.Data.Connector.Sqlite` · `Sylin.Koan.Mcp` |
| Entity vocabulary | `[Access]` + `[McpEntity]` on `Approval : Entity<Approval>`; `ApprovalsController : EntityController<Approval>` |
| Runtime dependencies | one local SQLite file; STDIO needs no network service, remote MCP needs authenticated HTTP |
| Inherited constraints | store parity; exposure is not authorization; MCP and HTTP must share access, tenant, and lifecycle policy |
| Proof | save/read through HTTP; inspect caller-visible MCP operations; assert SQLite won; deny one forbidden mutation |

Build path: [store and expose](../recipes/store-and-expose.md) +
[let an outside agent use the app](../recipes/let-an-agent-use-my-app.md). **Runnable exemplar:**
[FirstUse](https://github.com/sylin-org/koan-framework/blob/main/samples/FirstUse/README.md).

## Related task board: parents, bounded APIs, and cached lookups

| Receipt field | Composition |
|---|---|
| Package union | `Sylin.Koan.App` · `Sylin.Koan.Data.Connector.Sqlite` · `Sylin.Koan.Cache` |
| Entity vocabulary | `User`; `[Cacheable] Category`; `Todo` with `[Parent]` edges to both; `TodoItem` with a parent `Todo`; paginated `EntityController<T>` declarations |
| Runtime dependencies | one local SQLite file; process-local cache unless a shared cache connector is deliberately added |
| Inherited constraints | a parent edge is not a foreign key or cascade; child reads are bounded; cache keys and relationships inherit tenant scope |
| Proof | create the graph; read one parent plus a page and stream of children; reject an unbounded route; prove deletion invalidates cached lookup state |

Build path: [store and expose](../recipes/store-and-expose.md) +
[model things that relate](../recipes/model-things-that-relate.md) +
[make repeated reads fast](../recipes/make-repeated-reads-fast.md). **Runnable exemplar:**
[TaskGraph](https://github.com/sylin-org/koan-framework/blob/main/samples/fundamentals/TaskGraph/README.md).

## Local semantic knowledge hub

| Receipt field | Composition |
|---|---|
| Package union | `Sylin.Koan.App` · `Sylin.Koan.Data.Connector.Sqlite` · `Sylin.Koan.AI` · `Sylin.Koan.AI.Connector.Onnx` · `Sylin.Koan.Data.AI` · `Sylin.Koan.Data.Vector` · `Sylin.Koan.Data.Vector.Connector.SqliteVec` |
| Entity vocabulary | `[Embedding] KnowledgeItem : Entity<KnowledgeItem>`; ordinary saves index; a bounded controller uses `Client.Embed` then `Vector<KnowledgeItem>.Search` |
| Runtime dependencies | ONNX model and vocabulary files beside the executable; local SQLite data and vector files; no model or vector service |
| Inherited constraints | indexing and querying use the same model and dimensions; model/version change requires re-indexing; old rows are not indexed retroactively |
| Proof | save clearly different items and assert a known neighbour ranks first; prove ONNX and sqlite-vec won; missing model files fail clearly |

Build path: [turn an idea into a running app](../recipes/poc-an-idea.md) +
[search by meaning](../recipes/search-by-meaning.md). **Runnable exemplar:**
[GardenCoop Local Discovery](https://github.com/sylin-org/koan-framework/blob/main/samples/journeys/GardenCoop/02-LocalDiscovery/README.md).

## Tenant photo library with durable ingest

| Receipt field | Composition |
|---|---|
| Package union | `Sylin.Koan.App` · `Sylin.Koan.Data.Connector.Sqlite` · `Sylin.Koan.Tenancy` · `Sylin.Koan.Web.Auth` · `Sylin.Koan.Web.Auth.Connector.Test` · `Sylin.Koan.Jobs` · `Sylin.Koan.Storage` · `Sylin.Koan.Storage.Connector.Local` · `Sylin.Koan.Media.Core` · `Sylin.Koan.Media.Web` · `Sylin.Koan.Web.Sse` |
| Entity vocabulary | tenant-scoped `Event`; `[StorageBinding] PhotoAsset : MediaEntity<PhotoAsset>` with `[Parent]`; `UploadStaging : StorageEntity<UploadStaging>`; `PhotoProcessingJob : Entity<PhotoProcessingJob>, IKoanJob<PhotoProcessingJob>` |
| Runtime dependencies | SQLite plus owned local storage; an authenticated tenant selector; disk backup remains the operator's responsibility |
| Inherited constraints | original bytes are never overwritten; staging is removed only after success; tenant scope crosses HTTP, Jobs, data, and storage; SSE reports progress but does not make work durable |
| Proof | upload returns a Job receipt; restart mid-ingest; fetch original and derivative only inside the owning tenant; deny cross-tenant access and retain staging after failure |

Build path: [accept and serve files](../recipes/accept-and-serve-files.md) +
[run work in background](../recipes/run-work-in-background.md) +
[isolate tenants](../recipes/isolate-tenants.md). **Runnable exemplar:**
[SnapVault](https://github.com/sylin-org/koan-framework/blob/main/samples/applications/SnapVault/README.md).

To add content search, inherit the entire [semantic-search](ai/semantic-search.md) receipt, including a
real vision/embedding provider and re-index policy; the upload receipt alone does not prove AI.

## Trusted customer record from conflicting arrivals

| Receipt field | Composition |
|---|---|
| Package union | `Sylin.Koan.App` · `Sylin.Koan.Data.Connector.Json` · `Sylin.Koan.Canon` · `Sylin.Koan.Canon.Web` |
| Entity vocabulary | `Customer : CanonEntity<Customer>` plus matching, validation, enrichment, and conflict contributors |
| Runtime dependencies | local JSON data on the compiled floor; a human owner for ambiguous matches |
| Inherited constraints | identity and conflict policy belong to the application; ambiguous matches do not guess; commit checkpoints are ordered but not one atomic rollback |
| Proof | replay the same arrival without duplication; inspect field provenance; reject an ambiguous match; surface the exact failed checkpoint |

Build path: [reconcile messy arrivals](../recipes/reconcile-messy-arrivals.md). **Runnable exemplar:**
[CustomerCanon](https://github.com/sylin-org/koan-framework/blob/main/samples/applications/CustomerCanon/README.md).

## Editorial publication channel

| Receipt field | Composition |
|---|---|
| Package union | `Sylin.Koan.App` · `Sylin.Koan.Data.Connector.Sqlite` (one connector, two configured sources on the local path) |
| Entity vocabulary | one `Article : Entity<Article>`; ordinary draft operations use the default; publish and withdraw use `EntityContext.Source("Published")` |
| Runtime dependencies | two distinct SQLite files, each included in backup and restore ownership |
| Inherited constraints | publication is an idempotent copy, not a mirror, status, transaction, or default-store cutover |
| Proof | publish an approved subset twice without duplicates; prove both physical sources; make Published unavailable and assert no fallback write reaches Default |

Build path: [publish to a named channel](../recipes/publish-to-a-named-channel.md). **Runnable exemplar:**
[DevPortal](https://github.com/sylin-org/koan-framework/blob/main/samples/applications/DevPortal/README.md).

## Grow the same application

1. [POC](../recipes/poc-an-idea.md) - smallest local, seeded, meaningful slice.
2. [Prototype](../recipes/share-a-prototype.md) - sign-in, access declarations, and a reachable URL.
3. [Production](../recipes/harden-for-production.md) - verified provider graduation, secrets,
   observability, recovery ownership, and an honest hardening receipt.
