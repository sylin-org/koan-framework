---
type: SPEC
domain: framework
title: "R09-02 - Compile Host Constitution and Semantic Activation"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: build-fed activation evidence, one host constitution, one Communication module lifecycle, canonical activation facts, and package/source ownership
---

# R09-02 — Compile host constitution and semantic activation

- Tranche: `T7A — semantic composition prerequisite`
- Status: `passed`
- Depends on: R09-01 and ARCH-0115
- Unlocks: Core model authority and optional ZenGarden contribution migration
- Owner: Core build/discovery descriptors, host composition session, activation compiler, safe
  active/inactive projection, and rejected-problem emission

## Business sentence

> I reference the Koan capabilities my application needs and call `AddKoan()`; Koan activates exactly
> that constitution, leaves compatibility-only code inert, and tells me what it understood.

## Complete application expression

The common path adds no application concept:

```csharp
builder.Services.AddKoan();
```

Application package/project references remain the declaration of capability intent. A developer,
coding agent, or operator may inspect the existing startup/facts surface, but no new attribute,
registry, provider selector, module class, contributor call, or configuration stanza is required.

Dependency-only bundles, direct connectors, and deliberate application declarations count as intent.
An incidental transitive contracts/compatibility assembly does not.

## User-language gate

The reference comparison for every design choice in this slice is Koan's golden business-to-code path:

```csharp
builder.Services.AddKoan();
public sealed class Todo : Entity<Todo> { }
public sealed class TodoController : EntityController<Todo> { }
```

The point is not three lines; it is that each line names one application intent and framework
machinery stays behind those intents. R09-02 passes only if activation remains equally legible to a
human or coding model: reference capability, call `AddKoan()`, observe the compiled constitution.
Compiler, descriptor, contribution, election, and lifecycle vocabulary belongs to framework/module
authors and diagnostics, never the common application path. Any new public concept must remove more
cognitive branches than it adds and must be discoverable from the business concept that needs it.

## Guarantee and correction

### Guarantee

- on the supported build path, one host-owned constitution classifies stable descriptors as available
  plus active, inactive, or rejected with exact declaration evidence;
- inactive owner or target descriptors are filtered before their implementation constructor/factory;
- active descriptors validate identity, hard dependencies, and ordering, then all active module
  factories complete before the first migrated module registration;
- one generated module instance is retained for registration and startup, while the immutable
  constitution owns its activation/fact identity;
- two service collections in one AppDomain own distinct constitutions, module instances, decisions,
  and activation facts;
- the current fact envelope projects the activation decisions; it does not rediscover them; and
- repeated normal operations do not rerun descriptor discovery or activation.

### Corrective failure

Duplicate IDs, missing hard dependencies, invalid bundle exports, self-edges, cycles, and descriptor
factory failures stop composition before migrated module registration and surface a stable
`SemanticProblem` with owner, reason, and safe correction. A supported build that promises but lacks its
resource fails earlier with a corrective `InvalidDataException` instructing clean/rebuild. A host that
never imported the build target remains an explicitly unsupported compatibility fallback: available
descriptors activate with `degraded-fallback` evidence. Errors name the unmet application intent; they
do not ask an application author to edit a registry or election engine. Module constructors are required
to be side-effect-free; literal rollback of arbitrary constructor behavior is not an honest guarantee.

## Meaningful user-visible result

An unchanged `AddKoan()` host reports deterministic direct/bundle/transitive activation evidence through
the existing runtime-facts envelope. A dormant proof module/contribution is present but provably never
constructed. An active generated module is constructed once for registration and startup. Its
descriptor, constitution decision, startup listing, and fact projection share one identity; the instance
does not recompute or own that projection. Web and MCP facts agree because both read the same store/model.

This is an operational and agent-facing result, not preparatory scaffolding.

## Focused assessment

### Current owners

- `Sylin.Koan.Core.targets` emits flat module and exact direct-reference manifests plus an ordinary
  dependency graph for source and package references.
- `RegistrySourceGenerator` and `RegistryManifestLoader` mutate process-static `KoanRegistry` type sets.
- `AppBootstrapper` loads the entire reference closure and parameterlessly constructs every discovered
  initializer; direct-reference evidence is registered but is not its activation gate.
- `KoanModule` may be constructed separately for registration, startup, and AppRuntime reporting.
- `AddKoan(Action)` runs baseline bootstrap before the declaration callback and therefore cannot freeze
  the model in `AddKoan()` itself.
- AppRuntime/provenance/composition reporting re-enumerates and reconstructs module meaning after boot.

### Target owners

- Build/package assets own immutable direct and bundle/dependency activation evidence.
- Generated immutable descriptors own available type/factory metadata.
- `SemanticCompositionSession` owns declarations for one `IServiceCollection` until provider/host freeze.
- `SemanticActivationCompiler` owns component validation, deterministic order, activation filtering, and
  decision/problem emission for one host.
- `SemanticHostConstitution` is the single immutable activation artifact; this slice does not create a
  second catalog/model pair.
- `SemanticModuleRuntime` retains the exact migrated instances used for registration and startup.
- Future typed target builders and pillar policies consume the constitution while retaining their
  specific combination and failure rules.

### Coalescence decision

Core centralizes only lifecycle law: stable identity, activation evidence, dependency/order validation,
pre-registration failure, host ownership, freezing, and safe reasons. It does not decide Data axes,
Communication routes, Cache identity, Tenancy, or provider fallback.

The existing deterministic Kahn ordering algorithm is absorbed and generalized over stable descriptor
IDs. The reporting-only `KoanCompositionBuilder` is not generalized.

Module registration, typed capability contribution, and structural plan caching are explicitly three
different phases. `KoanModule.Register(IServiceCollection)` runs before a provider exists; typed
contributors may require provider-owned dependencies; dynamic plan caching belongs to a real runtime
plan owner. R09-02 establishes the constitution and module phase only. The first optional ZenGarden
target earns `IContributeTo<TTarget>` in the next slice, and the first legitimate dynamic Data shape
earns the structural plan cache. One universal executor would obscure these lifecycle truths.

## Exact production contracts selected by exploration

Placement begins under `src/Koan.Core/Semantics/` so the existing `Composition/` reporting twin cannot
be mistaken for the new authority:

- internal `SemanticId`;
- internal `SemanticDecision`, `SemanticProblem`, and `SemanticEvidence`;
- internal `SemanticComponentDescriptor` and generated descriptor registry storage;
- internal `SemanticHostConstitution`;
- internal `SemanticActivationCompiler`;
- internal `SemanticCompositionSession`, evolved from the useful host-scoped behavior of
  `KoanCompositionScope`; and
- internal `SemanticModuleRuntime` retained by the existing module host.

The one unavoidable generated-code ABI and the module opt-in marker are public only because generated
third-party assemblies must call/compile against them; both are hidden from ordinary IntelliSense with
`EditorBrowsable(Never)`. `IContributeTo<TTarget>`, `KoanContributionAttribute`,
`SemanticApplicationModel`, and `SemanticPlanCache<TKey,TPlan>` are deliberately absent from this
slice. They become production types only with the first real consumer that proves their exact shape.

## Implementation sequence

1. Add focused red fixtures for direct project/package/bundle/transitive/missing-manifest activation,
   inactive construction, ID/dependency/order failures, two-service-collection isolation, one module
   instance, and constitution/facts parity. Capture constructor/reflection/DI-enumeration baselines.
2. Add one canonical shared build tool under `Koan.Core/build/tools/`. Ordinary project and package
   references are the sole source of dependency edges. The callable source-project target and generated
   `buildTransitive` props use that same graph; no authored
   duplicate props or runtime package scan is allowed.
3. Derive the members of `Sylin.Koan` and `Sylin.Koan.App` from their ordinary references, generate
   deterministic recursive dependency evidence for package consumers, and evolve the embedded resource to a
   versioned root/edge format. Prove source and staged-package forms are semantically identical while
   contracts-only `Data.Abstractions` remains activation-free because it contains no module.
4. Make generated descriptor/analyzer delivery automatic for a downstream package consumer. Detect its
   single concrete `KoanModule`, derive identity from `PackageId`/`AssemblyName`, and use a hidden registration ABI; generated and
   reflection-fallback descriptors coalesce by implementation type. A marked type is excluded from
   legacy initializer and auto-registrar sets before either path can construct it.
5. Introduce the host composition session, activation compiler, decision/problem model, one immutable
   constitution, and one module-runtime set. Compile metadata and construct all active migrated modules
   before the first migrated registration; preserve existing cross-module ordering with the shared Kahn
   law.
6. Migrate `KoanCommunicationModule`. Direct Communication and every legitimate bundle/dependency root
   must carry an export chain before cutover. Retain one captured instance for registration and startup;
   let descriptor/constitution identity drive startup and activation-fact projection; remove its legacy
   bridge/DI/AppRuntime construction paths; and delete its duplicated provenance `Report`
   reconstruction in favor of constitution identity plus existing Communication route/fact truth.
7. Run the application `AddKoan(Action)` callback after module registration, preserving application-last
   declaration/override behavior, but before the session freezes. Nested calls reuse the same session;
   repeated parameterless calls do not reconstruct or refreeze.
8. Verify the focused matrix and structural idempotence probes, delete superseded Communication paths,
   and record the exact optional-contribution restart point. Absolute allocation/reflection budgets wait
   for the first dynamic-plan consumer that has a real hot path to measure.

## Red proof matrix

| Cell | Required observation |
|---|---|
| Direct `ProjectReference` | intended owner active exactly once with project evidence |
| Direct staged `PackageReference` | same model identity and behavior with package evidence |
| Dependency-only bundle | exported members active with an evidence chain back to the direct bundle |
| Direct connector descriptor | deferred to the later connector/module-conformance slice; synthetic hard-dependency law is proved here |
| Transitive contracts/compatibility | descriptor available but inactive; factory/constructor count zero |
| Promised manifest missing | supported build fails early with a clean/rebuild correction rather than guessed activation |
| Build target never imported | explicitly unsupported compatibility fallback activates available descriptors with degraded evidence |
| Ordering | stable ordinal-ID tie; optional missing order edge ignored; hard dependency missing rejects; duplicate/self/cycle rejects before any migrated factory/registration |
| Two hosts | two service collections own distinct constitutions/facts/module instances; process-static registries expose availability only |
| Module lifecycle | one retained instance registers and starts once; the constitution projects its activation; no legacy or DI reconstruction |
| `AddKoan(Action)` | declarations are included before freeze; nested/parallel composition sessions remain isolated |
| Projection | successful startup/Web/MCP projections carry constitution decision IDs; rejected composition fails with the same bounded problem identity |
| Economy | one factory/activation per host; repeated parameterless `AddKoan()` calls retain the same runtime without reconstruction |

## Deletion targets in this slice

- AppRuntime parameterless module/report construction for the migrated generated lifecycle;
- composition/provenance reconstruction of migrated activation facts;
- type/AQN ordering as the canonical path for migrated descriptors; and
- any new temporary reflection/DI enumeration introduced during the vertical.

For Communication specifically this makes the module ineligible for generated/fallback legacy
initializer entries, the `KoanModule.Initialize` bridge, DI construction through
`IEnumerable<KoanModule>`, and AppRuntime provenance reconstruction; it also removes the module's
duplicated `Report` override. Existing Communication composition facts remain the runtime truth for
provider elections, bounds, handlers, subscriptions, and context carriage.

`IKoanInitializer`, `IKoanAutoRegistrar`, process-static registry facets, runtime manifest fallback, and
whole-assembly roots are not deleted until their remaining consumers migrate. They are explicitly
transitional and cannot become inputs to the new plan authority.

## Focused verification only

- Core semantic/ordering/module/facts tests added or directly affected by the slice;
- bootstrap direct/bundle/transitive/staged-package fixtures;
- AppHost/two-host and one-module-lifecycle cells;
- Communication local Transport/Events and Web/MCP facts parity cells;
- `dotnet build` for directly changed projects and one staged consumer; and
- docs/skills lint, `git diff --check`, privacy, and status review.

Do not run the release certification aggregate. Broader suites run only if a focused failure supplies a
specific cross-pillar reason.

## Acceptance

- The application expression remains package intent plus `AddKoan()`; no compiler vocabulary enters app
  code or normal agent instructions.
- Descriptor activation filters before implementation construction.
- Stable dependency/collision validation and all migrated factories complete before migrated module
  registration.
- One generated module instance is retained for registration and startup; the constitution is
  the sole activation/fact authority.
- One host constitution and decision ID feeds activation facts; no catalog/model twin or migrated
  reporting path recomputes it.
- Direct, bundle, dependency, and transitive evidence are distinguishable and deterministic.
- Two service collections remain isolated; no new mutable process semantic authority exists.
- The first production consumer and meaningful operator/agent result ship with the kernel.
- Structural idempotence evidence proves one factory/activation per host and no repeated `AddKoan()`
  reconstruction; absolute hot-path budgets remain with the first real dynamic-plan consumer.
- Every migrated old owner is deleted or reduced to a renderer in the same slice.
- No public typed-contribution or plan-cache type exists without its first real consumer.

## Stop conditions

- Stop if bundle activation requires runtime package scanning or treats the whole transitive closure as
  intent.
- Stop if inactive filtering needs contributor/module construction to discover identity or target.
- Stop if Core must name a pillar or accept an untyped universal contribution payload.
- Stop if `AddKoan(Action)` declarations can miss the freeze boundary.
- Stop if the slice creates a second model/fact authority or leaves two lifecycle instances for the
  migrated module.
- Stop if package-consumer descriptor generation depends on repository-only build props.
- Stop if Communication is migrated before direct, nested-bundle, and legitimate dependent-package
  activation chains are proven.
- Stop if R09-02 introduces `IContributeTo<TTarget>` or a plan cache without the real typed target or
  dynamic plan owner that can prove its contract.
- Stop before broad certification, publication, push, tag, release, or private downstream inspection.

## Kickoff record

- Date: 2026-07-16
- Starting branch/HEAD: `dev` at `546817ee0d3a`
- Preserve: the five pre-existing R08/packaging companion-document modifications and all intentional R09
  documentation; never stage `tmp/`.
- Exploration disposition: one shared build-export tool, one versioned reference/edge resource, one
  host constitution, and one Communication lifecycle. Typed contribution and plan caching are deferred
  to earned consumers.
- First implementation action: write the focused manifest/parser/activation/session red fixtures before
  editing production types.

## Implementation record

- One shared `Sylin.Koan.SemanticActivation.targets` compiles direct roots and ordinary recursive dependency
  edges for both source projects and packed bundles. Project/package references are the declaration; package
  props are derived automatically. `Sylin.Koan`, `Sylin.Koan.App`, and Communication-dependent package roots
  carry ordinary dependency paths, while contracts-only assemblies remain activation-free by containing no module.
- `Sylin.Koan.Core` ships the primary build target, shared activation target, and registry generator DLL
  as one `buildTransitive` unit. A staged out-of-repository consumer proved that the package executes
  the generator, emits `RegisterSemanticModule`, and omits the marked module from legacy initializer/
  auto-registrar output.
- A supported composition build emits both the versioned reference resource and a required-manifest
  assembly marker into configuration/TFM/RID-isolated intermediates. A missing marked resource fails
  with a clean/rebuild correction; an application that never imported the target remains an explicitly
  degraded compatibility fallback.
- Core now owns one immutable activation compiler/constitution, one service-collection composition
  session, one retained module runtime, and one shared stable topological-order implementation.
  Duplicate identities, missing hard dependencies, self/cycle edges, factory failures, and descriptor/
  runtime identity conflicts reject before registration. A failed initialization faults that service
  collection and requires a fresh host instead of silently freezing partially registered services;
  the same fail-closed rule covers application declaration failures after service mutation. Semantic
  registration failure cannot be swallowed by legacy lenient boot.
- Descriptor metadata is the single identity declaration for migrated modules. `KoanModule.Id` inherits
  it, and a conflicting override rejects. The retained semantic instance wins even if a legacy duplicate
  reaches DI.
- Communication is the first real vertical. Generated and reflection fallback discovery make it
  ineligible for the legacy initializer/auto-registrar sets; its duplicated `Report` override is gone;
  registration and startup use the retained semantic instance; activation facts come from the
  constitution. Framework-wide legacy bridges remain only for unmigrated modules.
- The existing fact store remains the one projection owner. Live FirstUse startup and
  `/.well-known/Koan/facts` showed `koan.semantic.component.active` for Communication, while the focused
  Web and MCP contracts each prove exact projection of the host-owned envelope.
- Packaging now assigns Core-owned generator sources and analyzer-project sources to every package
  whose bytes they may affect. External packed payloads must resolve to Git-tracked repository inputs;
  ignored, local, generated, and outside-repository bytes cannot silently enter release intent. Core's
  required build assets are validated with exact archive-entry casing so Windows cannot certify a
  package that Linux cannot import.

### Focused evidence

- Core semantic compiler/session: 22/22; focused module host/lifecycle: 5/5. Hard-dependency evidence
  preserves its full bundle-to-component-to-dependency path.
- Repository version-intent and external/analyzer input ownership: 22/22; exact-case Core package
  build-asset gate: 2/2.
- Activation build/package matrix: 5/6 in the closure run. Source and staged-bundle parity,
  configuration isolation, required-root correction, and the seven-root Communication conformance
  cell passed. The unchanged staged-Core generator consumer alone could not reach NuGet.org from the
  restricted sandbox; its prior focused 1/1 success remains the applicable evidence and is not
  restated as a new pass.
- Communication retained registration/configuration lifecycle: 2/2; its earlier descriptor/legacy
  exclusion plus focused local behavior cell remains 11/11.
- Web well-known facts: 1/1; MCP runtime-facts resource: 1/1 (restricted Windows required the recorded
  EventLog opt-out; PMC-025 owns that host-policy issue).
- FirstUse source build: zero warnings/errors; live semantic fact and startup module identity observed.
- No aggregate release-certification suite, publication, push, tag, or remote mutation was run.

### Deliberate boundary

The synthetic descriptor matrix proves hard-dependency and direct-component law; only Communication is
migrated in this child. Connector descriptors, typed contributions, provider compilation, and structural
plan caching remain future consumer-owned slices. Generator authoring diagnostics for inaccessible or
non-constructible marked module types belong to the later third-party conformance slice; current valid
source and staged-package paths are proved.

The later module/contributor-conformance slice owns the remaining generalization questions: unique
generated module reporting, conditional/memoized reflection fallback, atomic registry
registration, `SemanticId` comparer alignment, framework-author descriptor diagnostics,
`GenerateAssemblyInfo=false` marker posture, multi-TFM bundle specialization, and whether a
bundle-exported provider constitutes provider intent. None is required by or falsely claimed for the
single migrated Communication vertical.

## Closure record

- Passed: 2026-07-16.
- Independent adversarial review: no remaining code-level blocker in the declared R09-02 scope.
- User result: package intent plus `AddKoan()` now compiles one inspectable host constitution without
  adding application ceremony.
- Next earned consumer: R09-03 evaluates ZenGarden's optional-layer lifecycle and admits a typed
  contribution surface only if that user-visible proof requires it.
