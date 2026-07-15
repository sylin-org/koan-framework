---
type: REFERENCE
domain: data
title: "R03 Entity Language Inventory"
audience: [architects, maintainers, framework-authors, ai-agents]
status: current
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: consumer-visible Entity, extension, attribute, context, and lifecycle surface at e325ff58
---

# R03 Entity language inventory

This inventory records the language a consumer sees before R03 decides what belongs there. It is
descriptive, not an endorsement. The assessed snapshot is
`e325ff5899528a776893fe5aae11b4b9f6f58df4`.

## Current semantic center

[`Entity<TEntity,TKey>`](../../../src/Koan.Data.Core/Model/Entity.cs) is simultaneously:

- an identity-bearing base type (`Id`);
- an Active Record-style static data facade;
- an instance persistence and relationship facade;
- a host-owned lifecycle-composition point (`Entity.Lifecycle`);
- a type-level cache facade, even when the full cache module is absent;
- a host/service-locator bridge through `AppHost.Current` and cached static metadata.

The common string-key form, `Entity<TEntity>`, adds GUID-v7 identity generation. This produces the
low-ceremony business code Koan wants, but it also means the base type currently owns more than one
semantic role.

## Surface map

| Surface | Consumer language | Current owner | Initial classification |
|---|---|---|---|
| Identity | `Id` and string-key generation | `Entity<T,TKey>` | intrinsic entity semantic |
| Read/query | `Todo.Get`, `All`, `Query`, `QueryRaw`, streams, pages, count | `Entity<T,TKey>` / `Data<T,TKey>` | entity-set facet; useful, but type-level rather than instance behavior |
| Persistence | `todo.Save()`, `Upsert`, `Remove`, bulk operations, patch | Entity plus `AggregateExtensions` | intrinsic persistence facet, with duplicate aliases and unsafe broad overloads |
| Data movement | `Todo.Copy/Move/Mirror`, `todo.MoveToPartition` | Entity and data-core extension | operational/provider facet; not ordinary entity behavior |
| Relationships | `GetParent`, `GetChildren`, `GetRelatives`, collection `Relatives` | Entity and relationship extensions | entity-centered, but current execution can hide full scans |
| Lifecycle | `Todo.Lifecycle.BeforeUpsert/AfterRemove/...` | host-owned composition plan | entity-centered persistence hooks; registration and execution are owned by the exact host |
| Context | `EntityContext.Source/Adapter/Partition/Transaction`, cache behavior, typed slices | static AsyncLocal carrier | operation scope and infrastructure control plane mixed together |
| Cache | `[Cacheable]`, `todo.Uncache()`, `Todo.Cache.Flush()` | cache module plus a Data.Core partial | entity-centered policy/instance invalidation; type facade leaks without module |
| AI/vector | `[Embedding]`, `todo.FindSimilar()`, `SemanticSearch<Todo>()` | Data.AI extensions | `FindSimilar` is entity-centered; type search lacks a coherent Entity facet |
| Messaging | `message.Send()` for every class | Messaging extension | message-centered, not entity-specific; overly broad receiver |
| Jobs | job entity + `IKoanJob<T>` and job attributes | Jobs module | typed entity capability/facet, not a general entity verb |
| MCP/web | entity/type attributes and controller inheritance | MCP/Web modules | projection policy; declarative and external to domain instance behavior |
| Media | media entity + `media.Url()` | Media module | entity-centered only for the `IMediaObject` facet |
| Canon | canon entity + `Canonize`, `RebuildViews` | Canon module | `Canonize` is entity-centered; rebuilding views is a control-plane operation |
| Backup | `entity.BackupTo/RestoreFrom/ListBackups/DeleteBackup` | Data.Backup extensions | type/control-plane behavior misleadingly attached to an arbitrary instance |

## Static Entity vocabulary

The base type currently exposes these families directly:

- lifecycle: `Lifecycle` during host composition;
- reads: `Get`, `All`, `Query`, `QueryRaw`, `AllStream`, `QueryStream`;
- windows/counts: `FirstPage`, `Page`, `Count.Exact/Fast/Optimized/Where/Query/Partition`;
- writes: `Upsert`, `UpsertIfChanged`, `UpsertMany`, `Remove`, `RemoveByQuery`, `RemoveAll`,
  `Patch`, and `PatchMerge`;
- batching/movement: `Batch`, `Copy`, `Move`, and `Mirror`;
- provider fact: `SupportsFastRemove`;
- cache: `Cache.Flush/Count/Any`.

This is already large enough that adding every future module directly would make IntelliSense a feature
catalog rather than a business language. R03 therefore needs a facet rule, not a blanket ban or a
blanket invitation.

## Extension vocabulary

### Coherent receivers

- `Save`, `Upsert`, `UpsertId`, and `Remove` constrain the receiver to `IEntity<TKey>`.
- `FindSimilar` constrains the receiver to a string-key entity.
- `Uncache` constrains the receiver to the concrete Koan Entity base.
- `Url` constrains the receiver to `IMediaObject`.
- `Canonize` constrains the receiver to `CanonEntity<T>`.
- relationship helpers constrain the receiver to Entity/IEntity collections.

These are discoverable only when the declaring namespace is in scope. Referencing a project/package
does not currently inject module namespaces into a consumer through build-transitive global usings.
“Reference = Intent” activates runtime composition, but does not by itself guarantee that every new
extension appears in IntelliSense without an import.

### Broad or misleading receivers

- [`Upsert(this object)` and `Delete(this object)`](../../../src/Koan.Data.Core/AggregateExtensions.cs)
  attach persistence verbs to every object whenever `Koan.Data.Core` is imported, then use reflection
  to reject non-entities at runtime.
- [`Send<T>(this T) where T : class`](../../../src/Koan.Messaging.Core/MessagingExtensions.cs) attaches
  messaging to every class whenever `Koan.Messaging` is imported. The receiver should express a
  message contract or the method should live on an explicit message facet.
- [backup extensions](../../../src/Koan.Data.Backup/Extensions/EntityBackupExtensions.cs) attach
  type-wide backup/restore/catalog operations to an arbitrary entity instance; the receiver is unused
  for identity and state.
- at inventory time the same backup surface contained `DeleteBackup`, which returned `true` from a
  placeholder rather than performing deletion. R04-01 now makes it fail loudly without touching storage.
- `RebuildViews(this entity, IServiceProvider, ...)` uses an entity instance to trigger a type-wide
  administrative operation and exposes DI plumbing in application code.

## Context and lifecycle findings

[`EntityContext`](../../../src/Koan.Data.Core/EntityContext.cs) is a well-tested ambient carrier with
inherit-unless-overridden nesting. It currently combines four different classes of concern:

1. business/request identity slices owned by modules, such as tenant or classification;
2. logical data scope, especially partition;
3. unit-of-work/transaction lifetime;
4. infrastructure overrides, especially source, adapter, and cache behavior.

The first three may belong in a coherent operation context. Direct provider selection and cache bypass
are expert escape hatches and should not define the default business vocabulary. The current static
AsyncLocal API also needs explicit capture/suppress/restore and host-isolation rules for jobs, agents,
parallel tests, and background dispatch.

At inventory time, the former `EntityEventContext<TEntity>` provided current value, lazy prior value,
operation state, cancellation, protection, and cancel/proceed results, but its registry was static per
closed generic type. R07-05 replaced that implementation with the host-owned
[`EntityLifecycleContext<TEntity>`](../../../src/Koan.Data.Core/Lifecycle/EntityLifecycleContext.cs)
and separated persistence Lifecycle from future domain Events/Transport intent.

## Concrete semantic hazards

| Hazard | Evidence | Why it matters |
|---|---|---|
| Capability visible when unavailable | `Entity<T>.Cache` ships in Data.Core and resolves cache services at call time | IntelliSense promises a capability that the referenced modules may not provide. |
| Namespace import is an unstated prerequisite | no package build target contributes module global usings | Referencing intent and discovering the extension are currently separate steps. |
| Runtime-only rejection | `Upsert/Delete(this object)` and `Send(this class)` | Agents and developers receive plausible verbs on invalid receivers; errors arrive too late. |
| Misleading instance semantics | backup and view-rebuild operations ignore the receiver's identity | Reading code suggests one record is affected when the operation is type-wide. |
| False success | `DeleteBackup` was a placeholder returning `true`; R04-01 replaces it with an explicit faulted task | The unsafe success is closed; managed deletion remains unsupported. |
| Hidden provider cost | relationship child helpers load all records and filter in memory | Business-readable code conceals an unbounded operation without negotiation or warning. |
| Host-global state | lifecycle registries and relationship metadata are static/cached | Repeatable hosts, tests, and agent sandboxes can observe cross-host residue. |
| Duplicate grammar | `Save`/`Upsert`, instance/static forms, generic/string forms | Useful convenience has grown into overload and documentation cost. |

## Initial keep / reshape / move / remove disposition

- **Keep:** `Id`, entity static reads/writes, instance `Save/Remove`, strongly constrained module
  extensions, declarative projection attributes, and an entity lifecycle vocabulary.
- **Reshape:** group type-level operations into small capability facets; split business context from
  provider overrides; make lifecycle registration host-owned; name cost/capability differences.
- **Move off Entity:** backup/restore catalogs, view rebuilds, provider elections, topology management,
  health, migrations, and other application/operator control planes.
- **Remove or strongly type:** object-wide persistence extensions, class-wide messaging extensions,
  success-shaped placeholders, and extensions whose receiver is semantically irrelevant.
- **Preserve as escape hatches:** `Data<T,TKey>`, repositories, raw queries, explicit adapter/source
  scopes, and services. They should remain available without crowding the common Entity language.

## Questions the contract must settle

1. Does an operation act on this entity, this entity type/set, or the application's infrastructure?
2. Can its receiver and constraints make invalid use fail at compile time?
3. Does adding the module make the surface discoverable without importing a broad namespace?
4. Can the operation explain selected provider, capabilities, cost class, and fallback?
5. Is behavior stable across hosts, scopes, tests, jobs, HTTP requests, and agent calls?
6. Is a type facet (for example `Todo.Semantic` or `Todo.Cache`) clearer than another top-level verb?
7. Which aliases are worth their overload cost, and which need a deprecation path?
8. Where is the transaction boundary for lifecycle hooks, domain events, projections, and integration
   events?
