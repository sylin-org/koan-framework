---
type: ARCHITECTURE
domain: data
title: "R04 Entity Facet Candidate Slate"
audience: [architects, maintainers, developers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
  status: reviewed
  scope: pillar-owned candidates for R04-07 consumer proof; no public API implemented
---

# R04 Entity facet candidate slate

This slate elects the Entity-language candidates that R04-07 should prove. It is a design input, not
a claim that the sketched members exist today. The historical
[`R03-ENTITY-INVENTORY.md`](R03-ENTITY-INVENTORY.md) remains the description of the current surface;
the canonical [Entity Semantics Contract](../../architecture/entity-semantics-contract.md) remains
the admission gate.

The organizing rule is:

> Koan's pillars own and explain capabilities. Only capabilities whose subject is an Entity may grow
> the Entity language.

Pillar symmetry is not a reason to add syntax. The delightful result is a small, predictable language
where a package reference makes relevant capability appear, invalid receivers never see it, and the
same capability name is recognizable in IntelliSense, startup reporting, operation explanation,
errors, health, and agent schemas.

## Delight test

A candidate wins only when it improves all three readings of the application:

| Reader | Delight requirement |
|---|---|
| Application developer | Starting from an Entity reveals the next meaningful business operation without registration code, infrastructure plumbing, or a catalog of unrelated verbs. |
| Coding agent | The type system and XML documentation expose valid receivers, effects, prerequisites, cost, cancellation, and failure; unavailable modules disappear at compile time. |
| Operator or reviewer | The operation name identifies its pillar, and the runtime can explain provider election, fallback, cost class, side effects, and correlation from the same facts. |

An attractive one-liner that hides an unbounded scan, remote model call, missing provider, or
control-plane mutation fails this test.

## Elected language

The examples in this section are **target grammar**, not current supported syntax.

### 1. Intrinsic Entity grammar: direct where a facet adds no meaning

| Semantic | Elected shape | Why it is delightful |
|---|---|---|
| Identity and persistence | `Todo.Get(id)`, `todo.Save()`, `todo.Remove()` | These are the shortest universally meaningful Entity operations. `Todo.Data` would add ceremony without adding information. |
| Set reads and bounded queries | `Todo.Query(...)`, `Todo.Page(...)`, `Todo.Count` | The Entity type is already the set receiver. Cost and boundedness belong in overloads, facts, and explanation rather than a redundant pillar noun. |
| Relationships | direct receiver-local verbs initially; consider `todo.Relations` only if cost-safe breadth requires grouping | Relationships are Entity semantics inside Data, not a separately referenced pillar. R04-06 now makes child-edge cost explicit and bounded; R04-07 can judge language shape without inheriting a hidden scan. |
| Operation context | `EntityContext.Transaction(...)` and named module scopes such as `Tenant.Use(...)` | Context is the third Entity-language location, but it scopes a logical flow rather than one record. `todo.Context` would imply false ownership. |

### 2. `Events`: elected intrinsic flagship facet

Candidate shape:

```csharp
Todo.Events.BeforeUpsert(...);
todo.Events.Raise(new TodoCompleted(...));
```

`Events` earns a facet even though it is intrinsic rather than module-contributed. It is a natural
place for a developer or coding agent to discover both how an Entity type participates in persistence
and which business facts an Entity instance may raise. Static versus instance access carries useful
meaning without adding another concept:

- `Todo.Events` owns ordered lifecycle composition for the Todo type;
- `todo.Events` owns domain facts raised by this Todo instance;
- lifecycle hooks, domain events, framework reactions, and integration messages remain distinct
  mechanisms with different timing and delivery promises;
- `Raise` records a typed fact in the current unit of work; it never means immediate broker delivery;
- handler ownership, order, transaction phase, outbox participation, and failures use the common
  composition/explanation facts;
- a disposed or missing host fails correctively rather than retaining a static provider or handler.

The current `Todo.Events` lifecycle grammar is the compatibility anchor. Instance event raising waits
for host ownership and transaction-boundary proof; the election does not claim it exists today.

### 3. `AI`: elected module-grown flagship facet

Candidate shape:

```csharp
var related = await Todo.AI.Search("food security", ct);
var similar = await todo.AI.Similar(ct: ct);
var vector = await todo.AI.Embed(ct);
```

`AI` wins over the earlier illustrative name `Semantic` because the retained entity-AI surface is
broader than search: it includes embedding and entity-context operations, and the name honestly warns
that a negotiated, potentially remote, costly, or non-deterministic capability is involved. Raw vector
administration, model migration, failed-job cleanup, and provider controls remain expert/control-plane
APIs.

Admission rules:

- the facet is declared by `Koan.Data.AI`, never predicted by Data.Core;
- the receiver is an actual Koan Entity, not the current `where TEntity : class` surface;
- search/similarity requires a string-key Entity until a different identity contract is proven;
- OCR appears only for a typed binary/media-capable receiver, not every Entity;
- the static and instance forms project the same `AI` capability facts;
- missing AI/vector prerequisites fail through the R04-05 fact model with no silent fallback;
- convention-derived content and automatic `[Embedding]` lifecycle behavior remain distinguishable.

This is the flagship demonstration of module-grown IntelliSense, but not the first migration: its
backend and lifecycle semantics make it a poor test-infrastructure pilot.

### 4. `Cache`: elected pilot facet

Candidate shape:

```csharp
var policy = Todo.Cache.Explain();
await todo.Cache.Evict(ct);
await Todo.Cache.Evict(id, ct);
```

`Cache` is the best first consumer-proof pilot because the scope is naturally visible, Data.Core
currently leaks a cache facade when the module is absent, and non-destructive inspection can prove
reference-activated syntax before mutation is migrated.

Admission rules:

- the facet exists only when `Koan.Cache` is referenced;
- `Todo.Cache` means policy and entries for the Todo Entity type, never cluster-wide cache control;
- `todo.Cache` means the cache identity derived from this instance and the current managed scope;
- the pilot begins with explanation/inspection; eviction follows only with R04-05 error facts and
  host-owned runtime resolution;
- flush-all, tags, topology, coherence, tracing, and provider administration remain on the Cache
  control plane;
- an unset Entity identity makes instance eviction an explained no-op, not false success.

### 5. `Media`: elected constrained facet

Candidate shape:

```csharp
var url = await photo.Media.Url(ct: ct);
var derivative = await photo.Media.Ensure(thumbnail, ct);
```

`Media` earns a facet only where media identity is real. It groups a growing family—delivery,
derivatives, transforms, and ancestry—without placing those verbs on ordinary entities.

Admission rules:

- the receiver must implement the narrow media contract (currently `IMediaObject` or its eventual
  replacement);
- URL generation explains storage/profile election, expiry, and authorization-relevant behavior;
- transformation/task operations must expose async/durable execution semantics rather than looking
  like local pure methods;
- storage topology, CDN policy, pipeline administration, and global task catalogs stay off Entity.

Media follows the AI/Cache compile infrastructure. It is not part of the R04-07 pilot.

## Elected direct or specialized language, not facets

| Pillar/semantic | Disposition | Reason |
|---|---|---|
| Canon | Keep a constrained direct instance verb such as `entity.Canonize(...)`; move `RebuildViews` off the instance. | One unmistakable business transformation does not benefit from `entity.Canon.Canonize()`. The specialized `CanonEntity<T>` receiver already provides discovery. |
| Storage | Keep creation/read/write verbs on the specialized storage model; reconsider a facet only if ordinary entities gain a typed storage relationship. | `StorageEntity<T>` already states the capability. Adding `.Storage` would repeat the type unless a second receiver shape appears. |
| Relationships | Keep within intrinsic Data grammar and reshape for boundedness before naming breadth grows. | A pillar facet would obscure that relationships are persistence semantics and could beautify an unsafe scan. |

This is intentional asymmetry. Pillars govern ownership; they do not impose needless nesting.

## Not elected onto generic Entity

| Pillar/surface | Entity admission | Correct home |
|---|---|---|
| Messaging | rejected | A typed message/envelope or explicit bus. Replace `Send(this T) where T : class`; Entity domain events do not send brokers directly. |
| Jobs | rejected by default | A job/action/workflow type and Jobs client. A future Entity facet requires a real entity-specific job relationship, not mere payload usage. |
| Web | rejected | Explicit controller/projection policy and attributes. Persistence never grants HTTP exposure. |
| MCP | rejected | Explicit, authorized agent projection. Entity capability never automatically becomes an agent tool. |
| Auth and tenancy | rejected | Authorization/projection policy and named operation-context scopes. They govern access; they are not instance behavior. |
| Backup/restore | rejected | Typed maintenance/control-plane service with receipts, scope, authorization, and audit. Never attach a type-wide catalog operation to an arbitrary instance. |
| Vector internals | rejected from the common path | `Todo.AI` for business semantic operations; explicit vector APIs for indexes, models, migration, and expert control. |
| Orchestration and observability | rejected | Host/operator control plane, startup report, health, logs, traces, and explanation resources. |
| Provider/source election | rejected | Automatic negotiation plus explicit expert scopes. Backend names do not belong in ordinary business code. |

## Candidate priority

| Order | Candidate | R04 role | Proof that earns the next step |
|---|---|---|---|
| 1 | `Cache` | compile-infrastructure pilot | Base absence, module presence, invalid receiver, all-module collision, removal, XML docs, and non-destructive runtime explanation. |
| 2 | `Events` | intrinsic user-delight flagship | Existing static lifecycle grammar remains compatible; instance facts are typed, host-owned, transaction-aware, inspectable, and never imply broker delivery. |
| 3 | `AI` | module-grown user-delight flagship | Static and instance facets, honest backend negotiation/cost, convention explanation, repeated-host isolation, and constrained OCR. |
| 4 | `Media` | narrow-interface proof | Facet appears only on media-capable entities and explains storage/delivery effects. |
| 5 | Canon direct verb plus receiver cleanup | asymmetry proof | Direct constrained verb remains clearer; administrative instance extension is removed or forwarded safely. |
| 6 | Broad receiver cleanup | language hygiene | `this object` persistence and `where T : class` messaging no longer advertise invalid operations. |

R04-07 still stops after one facet pilot and one broad-receiver repair. This ordering elects the
destination without authorizing a mass migration.

## One capability identity everywhere

The facet noun is not merely syntax. For every elected intrinsic or module facet, R04-05 and R04-07
should define one stable capability identity projected into:

- IntelliSense and XML documentation;
- startup composition and backend election reporting;
- per-operation explanation, structured logs, traces, and health;
- corrective exceptions and stable error codes;
- lock/composition artifacts and test assertions;
- MCP or other agent schemas only after explicit projection and authorization.

This is where user delight becomes trust: code completion proposes the same capability the runtime
later explains.

## Open naming checks before implementation

The election fixes the semantic owners and preferred nouns, but consumer proof must still settle:

1. whether the public C# spelling is `AI` or `Ai`; the product vocabulary favors `AI`, while framework
   identifier consistency must be tested across XML docs and generated schemas;
2. whether `Evict`, `Remove`, or another Cache verb best distinguishes a cache action from Entity
   deletion;
3. whether Media has enough proven breadth to justify a facet in its first migration, or should retain
   direct constrained verbs until then;
4. how static and instance facet accessors avoid allocations and host-owned service capture;
5. which typed domain-event contract and `Raise` result best expose transaction acceptance without
   suggesting delivery completion;
6. which compatibility forwarders can be safely deprecated without preserving dishonest receivers.

These are compile/API design checks, not reasons to reopen which capabilities belong on Entity.
