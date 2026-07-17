---
type: SPEC
domain: framework
title: "R09-08 - Coalesce Module Evidence and Execution Economy"
audience: [architects, maintainers, developers, ai-agents]
status: accepted
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: retained module/evidence lifecycle, standard identity and dependency derivation, contracts boundary, source/package parity, trim-shaped downstream generation, and focused pillar facts
---

# R09-08 — Coalesce module evidence and execution economy

- Tranche: `T7A — semantic composition prerequisite`
- Status: `passed`
- Depends on: R09-02 retained module lifecycle, R09-03 generated typed contributions, R09-07 canonical facts
- Unlocks: final R09 convergence and clean-room release handoff
- Owner: Core retained-module lifecycle; pillars own their evidence content and canonical plans

## Meaningful outcome

A referenced Koan or third-party capability uses one generated module instance for registration,
startup, provenance, typed structural contributions, and safe composition evidence. Startup does not
rediscover or parameterlessly construct a second reporter population. Known runtime operations execute
compiled plans without contributor discovery, reflection, DI enumeration, or structural mutation.

The application expression remains unchanged:

```csharp
builder.Services.AddKoan();
```

Deriving one concrete type from `KoanModule` is the complete ordinary module declaration. Identity and
activation are derived from standard project/package identity and dependency structure; authors do not
repeat them in a descriptor attribute or activation-edge metadata. Module authors override one
module-owned evidence method only when their concern has runtime decisions
to project. Application developers never implement a contributor or register a reporter.

All Koan-defined contracts consumed by another module live in an isolated contracts project containing
no `KoanModule`. Functional projects implement those contracts and contain activation. The slice must
delete—not introduce—`Inert` activation metadata; any edge that appears to require it identifies a
contract/package boundary to repair. The first concrete correction is S3's dependency on the
ZenGarden client contract: the contract surface belongs in `Koan.ZenGarden.Core`, while
`Koan.ZenGarden` retains the implementation and activation module.

## Focused discovery and coalescence assessment

**User's business sentence:** “Reference a capability and let Koan activate it once, run it efficiently,
and explain the exact result without extra application wiring.”

**Smallest honest application expression:** package/project reference plus `AddKoan()`. A third-party
module derives one concrete type from `KoanModule`; evidence is an optional override on that same module,
not another discoverable object.

### Current evidence

- `SemanticComponentDescriptor` and the registry generator already provide a construction-free factory,
  stable identity, exact typed-contribution delegates, dependency metadata, and deterministic ordering.
- `SemanticModuleRuntime` retains one active module instance per host and reuses it for registration,
  typed contributions, startup, and provenance.
- `KoanCompositionSnapshot.RunContributors` independently queries the process registry for
  `IKoanCompositionContributor`, parameterlessly constructs every result through `Activator`, and invokes
  it with a service locator. This is a second module-adjacent discovery/factory/lifecycle.
- The six implementations are framework-owned: Data, Cache, Communication, Jobs, Core segmentation, and
  Media. None needs an independently activated object. Five belong to an owning module; segmentation is
  Core's direct projection of `SegmentationPlan` plus realization receipts.
- Data projects the canonical default provider receipt, configured named sources, and diagnostics
  snapshots. Cache may materialize its boot policy and then projects topology/coherence/entity plans.
  Communication projects canonical route receipts/catalogs. Jobs projects ledger/wake/context posture.
  Segmentation projects the canonical plan and realization receipts. Media projects its materialized
  recipe catalog.
- The contributor scan runs on composition snapshot creation rather than on an Entity/message/cache/job
  hot path, but it remains reflection-sensitive, process-registry-coupled, trim-hostile, and capable of
  reporting inactive transitive assemblies.
- `IContributeTo<TTarget>` is a distinct structural compilation contract. It must remain generated and
  construction-free; runtime evidence must not be disguised as another structural target or rerun its
  compiler after the host freezes.

### Selected architecture

`KoanModule` is the one module-author lifecycle and gains one optional safe composition-evidence override.
`SemanticModuleRuntime` invokes that method over its retained, constitution-ordered instances. It catches
and identifies one module's reporting failure without compromising other evidence. There is no evidence
registry, evidence factory, discoverable marker, or DI enumeration.

Pillars retain ordinary internal static projectors where their reporting code deserves a focused file.
Those projectors are implementation details called by the owning module; they are not components,
services, contributors, or lifecycle owners. Core calls its segmentation projector directly because
segmentation is part of the always-present Core host plan rather than an optional module.

Data, Cache, Jobs, and Media migrate to ordinary concrete modules whose identity is generated from standard
package/assembly metadata so their retained instances can own evidence. Communication already satisfies that
requirement. Ordinary dependency edges carry transitive Communication participation for Cache and Jobs.
Source and package identities use the canonical `Sylin.Koan.*` form.

Specificity:

- framework: retained instance, deterministic invocation, failure isolation, generated descriptor ABI;
- pillar: evidence content, plan materialization policy, typed failures, and safe summaries;
- adapter: canonical receipts/capabilities exposed to its pillar plan;
- application: no evidence ceremony.

Disposition:

- keep: generated descriptor/factory, retained runtime, typed contribution compiler, canonical plans,
  `KoanCompositionBuilder` as the bounded safe projection writer;
- absorb: evidence invocation into the retained module lifecycle;
- rebuild: the six reporter classes as static concern-owned projectors or direct module methods;
- migrate: four legacy evidence-owning modules to canonical generated descriptors;
- delete: `IKoanCompositionContributor`, `[KoanDiscoverable]` reporting discovery, `RunContributors`,
  contributor Activator construction, and all six contributor implementations as lifecycle objects;
- reject: a replacement DI enumerable, evidence registry, second generated descriptor family, universal
  runtime plan, or compatibility alias preserving the old reporter path.

## Guarantee and corrective failure

- Only constitution-active retained modules may emit module evidence.
- One exact retained instance owns registration, structural contribution, startup, provenance, and
  evidence for one host.
- Two service collections never share module instances, evidence state, or compiled plans.
- One module's evidence exception yields one safe collection-failure fact naming that module; other
  module/Core evidence remains available.
- Generated descriptors invoke module factories and typed bindings directly. Runtime evidence invokes a
  virtual method on the retained instance; no contributor reflection or DI enumeration is used.
- Runtime Entity, Data, Cache, Communication, Jobs, and Storage paths do not call evidence projection or
  structural contribution machinery.

## Red proofs and deletion list

1. An active generated downstream module emits one custom fact from the same retained instance
   used for registration/start; an inactive transitive descriptor emits none and is never constructed.
2. Two independently composed hosts receive distinct module instances and distinct evidence.
3. A throwing module emits one bounded collection-failure fact while a sibling module and Core
   segmentation evidence remain visible.
4. Generated source contains direct module factories and typed contribution delegates; evidence needs no
   runtime-discovered implementer entry.
5. Focused trimmed publication starts and emits the downstream evidence without trimmer roots for a
   contributor implementation.
6. Source probes find no `IKoanCompositionContributor`, `RunContributors`, or composition-contributor
   `Activator.CreateInstance` path.
7. Existing Data, Cache, Communication, Jobs, segmentation, and Media fact assertions retain canonical
   election/guarantee/correction meaning after migration.
8. Delete all six old contributor lifecycle implementations and their public guidance in this slice.

## Scope

### In

- one optional module-owned composition-evidence override;
- retained-runtime evidence dispatch and per-module failure isolation;
- migration of Data, Cache, Jobs, and Media to generated retained modules;
- module-owned Data, Cache, Communication, Jobs, and Media evidence projectors;
- direct Core segmentation projection;
- deletion of the old contributor interface/discovery/Activator path;
- focused direct/transitive, multi-host, generated-source, trim, and no-hot-path-discovery proofs;
- one concise module-author conformance note; no application-facing reporting guide.

### Out

- eliminating every legacy `IKoanInitializer`/`IKoanAutoRegistrar` in the repository;
- eliminating reflection that belongs to unrelated Entity materialization, media recipe discovery, or
  the explicitly degraded manifest fallback;
- changing public Entity semantics, provider policy, or runtime guarantee content;
- universal operation model, release certification, publication, or remote mutation.

## Execution plan

1. Add red retained-module evidence tests for active/inactive, exact-instance, multi-host, and failure cells.
2. Add the optional module evidence method and retained-runtime dispatch.
3. Convert the six evidence sources and four legacy modules, preserving canonical plan/receipt inputs.
4. Delete the public contributor interface, discoverable scan, Activator path, and stale documentation.
5. Add downstream generated/trimmed conformance and source/economy ratchets.
6. Run only focused Core, Data composition, Cache topology, Communication composition, Jobs composition,
   Media composition, and packaging conformance cells.
7. Record the exact execution evidence and hand R09-09 the final convergence/deletion list.

## Implementation record

- A concrete `KoanModule` is the complete declaration. The registry generator derives identity from the
  standard NuGet `PackageId` with `AssemblyName` fallback, rejects multiple concrete modules in one assembly,
  and emits direct factories/contribution bindings without an authored descriptor attribute or module ID.
- Ordinary project/package references now produce one deterministic dependency graph for source and
  `buildTransitive` package consumers. Authored activation-edge metadata and its `Required`/`Inert` postures
  are deleted; source and staged-package manifests are byte-equivalent after transitive reduction.
- Cross-module contracts are structurally activation-free. ZenGarden client/watch/model contracts moved to
  `Koan.ZenGarden.Core`; S3 references that contracts assembly and no longer acquires the implementation.
- The retained module owns registration, contribution, startup, provenance, and safe evidence. The separate
  reporting contributor interface, registry scan, parameterless construction, and duplicate lifecycle are gone.
- Focused verification passed 68 tests: Core lifecycle/manifest/contribution 47, source/package/generator/
  contracts packaging 5, and Data/Cache/Communication/Jobs/Media evidence 16. Focused builds had no errors;
  three pre-existing XML-documentation warnings remain outside this slice. No release certification ran.

## Focused verification

- Core semantic activation/module lifecycle/composition snapshot specs;
- Core segmentation composition specs;
- Data composition lockfile specs;
- Cache Entity/topology composition specs;
- Communication provider/context/composition specs;
- Jobs composition/context specs;
- Media recipe composition specs;
- one downstream generated descriptor + trimmed publish probe;
- source inventory and `git diff --check`; no broad release suite.

## Stop conditions

- Stop if evidence creates a second module object, registry, factory, dependency graph, or DI enumerable.
- Stop if Core must reference a pillar type or interpret a pillar-specific fact code.
- Stop if a reporter recomputes a decision already owned by an immutable plan/receipt.
- Stop if inactive transitive modules can emit evidence or be constructed.
- Stop if compatibility preserves both old and new reporting paths.
- Stop before publication, push, tag, release, remote mutation, or private downstream disclosure.
