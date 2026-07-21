# ARCH-0115: Semantic Application Model and typed contribution compilation

**Status**: Accepted
**Date**: 2026-07-16
**Deciders**: Framework maintainer
**Scope**: Framework-wide composition mechanics, pillar-owned contribution languages, capability
overlays, immutable host plans, and their evidence projections.
**Related**: ARCH-0084 · ARCH-0105 · ARCH-0106 · ARCH-0111 · ARCH-0113 · ARCH-0114

---

## Decision

> **R09-05 implementation note (2026-07-16):** The first hard overlay is live. Tenancy contributes
> one `tenant` dimension; Data, Cache, and Storage consume the same host plan through pillar-owned
> realizations and report stable `enforced-or-rejected` coverage receipts. Ambient values bind once
> per operation and are absent from facts. Communication and Jobs carriage/route requirements remain
> subsequent slices and must not be inferred from state-pillar coverage.

> **R09-08 implementation note (2026-07-17):** A concrete `KoanModule` is the complete module
> declaration. Identity derives from standard `PackageId`/`AssemblyName`; ordinary references produce
> source/package dependency evidence. Cross-module contracts live in assemblies without a module, so
> consuming vocabulary cannot activate functionality and requires no special reference posture.

Koan will compile one host-owned **Semantic Application Model** from direct application intent,
active modules, typed capability contributions, pillar policy, provider capabilities, and explicit
configuration.

Framework-wide composition uses one generic contribution compiler. The compiler is generic over a
pillar-owned target; it does not contain a central switch or overload list naming Data, Cache,
Communication, Tenancy, or another domain. Pillars define typed contribution languages and compile
their own immutable execution plans. Capabilities implement one or more of those typed contracts.
Adapters realize the compiled requirements without interpreting business capabilities by name.

The architectural law is:

```text
application intent + active capabilities + pillar policy + provider evidence
    -> semantic catalog
    -> immutable host plans
    -> direct runtime execution
    -> facts, health, explanation, and proof projections
```

Koan compiles contributor objects while composing a host. Application operations execute only the
resulting plans.

## Why

Koan currently contains several strong but separate composition mechanisms: generated module
discovery, layered service-discovery contributors, Data axes, context carriers, Data and
Communication provider election, Entity cache plans, runtime-fact contributors, and adapter-specific
activation. Each solves real semantics, but shared mechanics are distributed and several concerns
learn about segmentation indirectly through Data-owned registries.

The V1 target is not less intrinsic complexity. It is one deliberate owner for each decision and one
place to change its mechanics. Application code should express the business while Koan deterministically
compiles, explains, and proves the infrastructure consequences.

## Application language is a design input

Koan designs a capability from the application inward. Before choosing an interface, registry,
builder, provider, or pipeline, the owning slice must state:

1. the user's business sentence;
2. the smallest honest C# expression and complete user action surface—references, code, decorations,
   configuration, and runtime prerequisites—of that sentence;
3. the guarantee that expression creates;
4. the observable correction when the guarantee cannot be met; and only then
5. the internal owners and compilation mechanics.

The golden example is a near 1:1 mapping from intent to code:

```csharp
builder.Services.AddKoan();
public sealed class Todo : Entity<Todo> { }
public sealed class TodoController : EntityController<Todo> { }
```

The three visible concepts mean three application decisions: use Koan, define a business entity, and
expose that entity through HTTP. This exact compactness is not possible for every concern, but it is
the comparison point. Any additional application-facing concept must represent a real business choice,
required guarantee, or deliberate override—not framework assembly, contributor, election, routing, or
diagnostic machinery.

This is an ergonomics and semantic-honesty requirement, not a line-count contest. The unit being
minimized is distinct application concepts and cognitive branches, not physical lines or tokens;
hidden references, attributes, configuration, context setup, and operational prerequisites count.
Omitting a required guarantee is not delight. Conversely, making developers configure consequences
that Koan can derive from intent is not honesty. The common path must be discoverable in IntelliSense,
readable by a human as business language, and structurally unambiguous to a coding model with limited
context. Advanced configuration remains available at the narrowest semantic owner and does not burden
the default path.

## Distinct information products

The Semantic Application Model supplies stable identities to several related products. They do not
become one mutable bag:

| Product | Question | Lifetime and cardinality |
|---|---|---|
| Semantic catalog | What can this application do and expose? | Host/version; low cardinality |
| Compiled host plan | What did this host select and how will it execute? | Host; immutable hot-path authority |
| Runtime facts | What was declared, activated, selected, rejected, or left unsupported, and why? | Current safe state |
| Health | Is the selected mechanism currently available? | Sampled current state |
| Operation receipt | What did one bounded semantic action cause? | Operation-scoped; bounded |
| Telemetry | How does behavior evolve over time? | External observability stream |
| Semantic diff | What application meaning changed between two versions or plans? | Comparison artifact |

Facts are not an event log. Health is not capability evidence. Telemetry is projected through
OpenTelemetry and ecosystem tooling rather than retained in a new Koan telemetry store. A semantic
diff compares canonical models; it does not run a second independent scanner.

## Specificity cascade

Every decision climbs only as far as its meaning remains identical:

| Level | Owns |
|---|---|
| Framework law | Activation evidence, stable identity, deterministic ordering, collision handling, host ownership, plan freezing, and decision reasons |
| Capability family | Reusable typed mechanics shared by related pillars, such as candidate election or context provenance |
| Pillar policy | Data, Cache, Communication, Jobs, Storage, Web, or AI semantics and validation |
| Adapter | Protocol conversion, physical mechanism, configuration, readiness, and provider-specific health |
| Application | Business rules, required outcomes, and deliberate expert overrides |

Similarity is insufficient reason to lift a mechanism. A shared substrate must be proven by at least
two real owners or by a foundational correctness requirement such as host isolation. Pillar semantics
never move into Core merely to make an API generic.

## Typed contribution model

The intended framework/module-author shape is equivalent to the following; exact public/internal type
names are an R09 implementation decision. This is not application API or application-author guidance:

```csharp
public interface IContributeTo<TTarget>
{
    void Contribute(TTarget target);
}

internal sealed class TenantModule : KoanModule,
    IContributeTo<SegmentationPlanBuilder>
{
    // Declares the tenant dimension once. Active pillars compile their own realizations.
}
```

The descriptor-backed retained module is the structural contributor. The framework does not create a
second contributor class, identity, factory, registry, dependency graph, ordering graph, or lifetime.
Generated module descriptors carry exact closed target bindings and direct apply delegates; reflection
creates equivalent bindings only in the explicitly degraded fallback path. The interface is invariant
so a base-target contribution cannot accidentally match a derived target or dispatch twice.

The generic compiler owns activation filtering, deterministic dispatch in constitution order, decision
evidence, cross-target finalization, and freezing. The target builder owns whether entries accumulate,
form an ordered chain, elect a winner, refine an obligation, or conflict. Every scheduled target is
compiled, validated, and frozen before any immutable plan is published. Dynamic provider/source objects
remain normal host DI services and never become structural contributors.

The runtime pillar consumes the generic compiler through composition. It does not inherit from a
framework base class. New pillars can define new typed targets, and new capabilities can implement
existing targets, without modifying Core.

Target compilation runs once after active modules register and application declarations complete, but
before the outer composition session freezes. Nested or repeated `AddKoan()` calls cannot compile a
target early or apply it twice.

When one meaning genuinely spans several pillars, it first compiles into a typed capability-family
model rather than forcing the capability module to reference every pillar target. Segmentation is the
first example: Tenancy declares the `tenant` dimension once; each active segmentable pillar is
responsible for compiling and proving its own realization. This prevents an N-pillar dependency fanout,
keeps optional implementation assemblies inert, and makes a future pillar responsible for becoming
segmentation-aware without reopening Tenancy. A truly pillar-specific extension still contributes
directly to that pillar's typed target through an inert contract.

Where a contribution contract must be public for third-party module authors, it lives in a clearly
infrastructural namespace/package and is kept out of normal application IntelliSense and agent guidance
where the platform permits. Application authors express capability intent, never contributor assembly.

This is a mandatory assembly boundary for every Koan-defined contract consumed across modules. The
contract lives in an isolated `*.Core`, `*.Abstractions`, or `*.Contracts` project containing no
`KoanModule`; the functional project references and implements it and owns activation. A consumer that
needs only vocabulary references only the contract project. `Inert` reference metadata is rejected as
a workaround for cross-module contracts placed in a functional assembly: move the contract and its
signature dependencies to the contract project instead.

## Activation law

A contribution is applied only when all required conditions hold:

```text
owning capability is directly activated
AND target pillar is active
AND a typed contribution is declared
AND its dependencies and applicability are satisfied
```

Provider satisfaction is evaluated afterward by the pillar:

```text
compiled obligation
AND elected provider can faithfully realize it
```

Contract or compatibility type presence is inert. Transitive reference closure cannot become direct
application intent. The constitution filters inactive modules before their one factory, so their
contribution bindings are never invoked. Removing a capability restores the baseline composition
without application glue.

Inertness is therefore structural, not annotated: a contracts-only assembly has no activation module;
a functional assembly does. Ordinary .NET project/package references carry the distinction.

ARCH-0114 remains the lifecycle precedent: `declared`, `active`, and `selected` are different facts.
ARCH-0115 generalizes the common compilation mechanics while preserving typed concern seams.

## Requirements and realizations

Capability-owned requirements and provider-owned claims remain distinguishable.

For example, Tenancy may require Cache operations to partition on an authenticated tenant dimension.
Cache compiles that requirement across reads, writes, eviction, tags, bulk invalidation, and coherence.
A Redis or Memory adapter executes the resulting cache identity. An adapter cannot prove isolation
merely by labeling itself tenant-aware, and a third-party contributor cannot relax Tenancy's hard
minimum.

Optional improvements and hard invariants retain different policies:

- ZenGarden automatic discovery may contribute a health-checked candidate and safely fall through.
- Explicit ZenGarden selection cannot silently become autonomous discovery.
- Tenancy segmentation is a hard invariant for every active applicable path and fails closed when no
  faithful realization exists.

The generic compiler records the outcome. The capability and pillar own whether absence means no-op,
fallback, warning, rejection, or boot refusal.

## Segmentation as a cross-pillar capability overlay

Tenancy is the flagship hard overlay. It owns the meaning and current tenant dimension. Each active
pillar owns enforcement at its chokepoint:

| Pillar | Example realization |
|---|---|
| Data | stamp/filter, container partition, or database route |
| Cache | key, namespace, eviction, tag, and coherence partition |
| Communication | authenticated context carriage and, when required, route/binding segmentation |
| Jobs | capture and restore before work-item load and handler invocation |
| Storage | key prefix, container, or physical placement |
| Web/MCP | trusted resolution, authorization, and safe projection |

The capability contributes the dimension once. Every active segmentable pillar consumes the compiled
family model, but no pillar names `tenant`; it understands dimensions, applicability, and required
coverage. A stable dimension identity allows deterministic composition and deduplication without
forcing one encoding across records, cache keys, queues, and blob names. Pillar-specific exceptions or
overrides remain typed contributions owned at that pillar rather than branches in the family model.

Communication reports isolation levels separately:

1. logical context isolation;
2. route or binding segmentation;
3. physical topology isolation; and
4. confidentiality.

Authenticated restoration proves the first, not the other three. A provider is eligible only for the
levels required by the compiled channel plan.

Future cross-pillar overlays may include region, residency, classification, retention, or access
subject, but they earn admission through real use and the same typed contracts. This ADR does not
create those features.

## Compile once, execute directly

Plans are host-owned. The structural plan is compiled for a stable scope such as an Entity type,
cache region, communication channel, provider, or operation shape. Ambient values such as the current
tenant are read by precompiled accessors during execution; they are not plan-cache keys and do not
cause contributor rediscovery.

Known structural plans should be compiled at host composition where practical. Legitimately dynamic
shapes may compile lazily once and memoize per host. Runtime paths must not enumerate DI, reflect over
contributors, rerun provider negotiation, or rebuild policy chains.

The contract is compatible with trimming and NativeAOT: discovery metadata may be generated, and
runtime plans should contain immutable descriptors and direct delegates rather than depend on
late-bound reflection.

## Inspectability and semantic honesty

The same compiled decision identities feed startup, machine-readable facts, health, errors, tests,
MCP, and future `plan`/`explain`/`doctor`/`diff` projections. Projections may redact or omit data based
on audience, but they do not recompute decisions.

For a hard overlay, Koan can produce a coverage matrix such as:

```text
tenancy/data             row-scoped             satisfied
tenancy/entity-cache     key-segmented           satisfied
tenancy/generic-cache    unsupported             rejected
tenancy/communication    authenticated-context   satisfied
tenancy/route            not-required            not selected
```

Dimension values, credentials, connection strings, and high-cardinality business data never enter
general composition facts.

## R09-05 migration outcome

> **R09-08 implementation amendment (2026-07-16):** Runtime evidence now follows the same retained
> descriptor-backed module lifecycle. `KoanModule.ReportComposition` projects canonical concern plans
> through the safe builder; `SemanticModuleRuntime` invokes only active retained instances and isolates
> reporter failures. The public `IKoanCompositionContributor`, registry discovery, parameterless
> Activator path, and six parallel reporter objects are deleted. Core segmentation projects directly;
> pillar reporting code is an ordinary static implementation detail. Structural
> `IContributeTo<TTarget>` compilation remains a distinct pre-freeze phase.

R09-05 resolved the first migration block as follows:

- `KoanCompositionBuilder` remains a safe reporting projection only; immutable concern plans are the
  runtime decision authorities.
- `DataAxisExpander` remains Data-local for predicate/operation semantics. It no longer supplies
  Tenancy or cross-pillar Cache/Storage identity; unknown Data-only fields are cache-excluded.
- `IKoanContextCarrier` proves opaque cross-pillar participation and ingress provenance; it should be
  represented in the semantic catalog without turning context carriage into routing isolation.
- Service discovery now proves the compiled target model: retained modules contribute once, the concern
  freezes one host plan, and live sources query per operation without a parallel contributor lifecycle.
- `EntityCachePlan` owns the Entity business key; `CacheIdentityPlan` owns physical segmentation for
  Entity and generic Cache operations without reading Data registries.
- Runtime facts project one value-free receipt per active state pillar. Startup, HTTP, and MCP consume
  that same envelope; unsupported Communication/Jobs coverage remains absent rather than implied.

`TenantAxis`, `TenantStorageGuard`, `ScopedEntityCacheKey`, and `StorageKeyScoper` are deleted without
compatibility aliases. Remaining greenfield slices follow the same single-owner deletion rule.

## Required conformance

Every contributable pillar and capability overlay proves, as applicable:

- declaration without activation is inert;
- direct activation applies exactly the intended typed contribution;
- transitive contract presence does not activate behavior;
- removal restores the baseline;
- ordering, collisions, and missing dependencies are deterministic and corrective;
- hard obligations fail closed when no provider can realize them;
- optional candidates follow pillar-owned fallback policy;
- multi-host composition does not share mutable plans or registrations;
- known runtime operations perform no contributor discovery or plan mutation;
- facts match the plan and expose no sensitive values; and
- the smallest application change yields a meaningful result with no integration glue.

ZenGarden is the optional-layer proof. Tenancy across Data, Cache, Communication, Jobs, and Storage is
the hard-overlay proof. An abstraction that cannot express both without embedding either policy is
rejected.

## Non-goals

ARCH-0115 does not introduce:

- one universal contribution payload or lowest-common-denominator DSL;
- one framework fallback policy;
- a central Core overload table naming every pillar;
- an inheritance hierarchy for runtime pipelines;
- per-request composition or mutable global registries;
- a universal operation/mediator model—the neutral operation model remains a V1.1 target;
- automatic destructive migration;
- a Koan-owned telemetry database; or
- compatibility preservation for pre-1.0 structures that obstruct the single-owner result.

## Consequences

- A referenced capability can change every relevant pillar coherently without application plumbing.
- Pillars remain semantically independent while sharing deterministic mechanics and evidence.
- Runtime paths become smaller and faster because composition cost is paid once.
- Operators and agents can distinguish understood, active, selected, satisfied, and unsupported
  behavior.
- Some current cross-pillar convenience built through Data registries will be deliberately broken and
  rebuilt under the correct pillar owners.
- Contributor contracts and activation evidence become durable framework extension points and need
  compatibility, AOT, privacy, and conformance discipline.

The execution and deletion ledger is [R09 — Compile the Semantic Composition Kernel](../initiatives/koan-v1/work-items/R09-semantic-composition-kernel.md).
