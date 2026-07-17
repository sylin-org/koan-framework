---
type: SPEC
domain: framework
title: "R09-03 - Compile One Optional Capability Layer"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: generic typed contribution lifecycle, compiled service-discovery plan, and ZenGarden optional-layer proof
---

# R09-03 — Compile one optional capability layer

- Tranche: `T7A — semantic composition prerequisite`
- Status: `passed`
- Depends on: R09-02 host constitution and semantic activation
- Unlocks: shared deterministic provider-selection mechanics and hard capability overlays
- Owner: Core contribution lifecycle, Core service-discovery target, and ZenGarden's optional layer

## Meaningful outcome

An application references ZenGarden and an already-compatible adapter automatically gains one
ZenGarden discovery layer. Removing ZenGarden restores that adapter's autonomous baseline. The
application adds no registration call, contributor type, binding, attribute, or connector glue.

The layer is compiled once for the host, can consult live topology when discovery actually runs, and
is explained consistently at startup and through runtime facts. Automatic discovery may fall through
according to the adapter's existing policy. An explicit `zen-garden://...` choice is a hard promise:
it resolves through the selected offering or fails with a safe, corrective error; it never silently
becomes localhost or another provider.

This is the first production proof of the generic typed-contribution lifecycle. The framework gains
that machinery only because one useful capability consumes it in the same slice.

## Why now

R09-02 established one host constitution, pre-construction activation filtering, generated semantic
descriptors, retained module instances, deterministic ordering, and canonical activation facts.
ZenGarden is the smallest honest next consumer because its product meaning is optional and reversible:
direct reference adds one layer, transitive compatibility remains inert, absence is a valid baseline,
and removal must leave no residue.

The current implementation already demonstrates useful candidate precedence, normalization, health,
and fallback behavior, but it invokes structural contributor objects per discovery, duplicates
adapter identity through ZenGarden-specific binding types, permits a redundant manual activation
path, and silently weakens explicit intent. Those are precisely the boundaries this slice can now
coalesce without widening into every provider or pillar.

## Evidence to read first

- Architecture: [ARCH-0114](../../../../decisions/ARCH-0114-layered-capability-activation.md),
  [ARCH-0115](../../../../decisions/ARCH-0115-semantic-contribution-compilation.md), and
  [R09-02](R09-02-host-constitution-and-semantic-activation.md).
- Generic semantic substrate: `KoanRegistry`, `RegistrySourceGenerator`,
  `RegistryManifestLoader`, `SemanticHostConstitution`, `SemanticActivationCompiler`,
  `SemanticDecision`, and `StableTopologicalOrder`.
- Discovery: `ServiceDiscoveryCoordinator`, `ServiceDiscoveryAdapterBase`,
  `IDiscoveryCandidateContributor`, and `DiscoveryContext`.
- ZenGarden: its legacy auto registrar, service-registration extension, initialization provider,
  offering bindings, discovery contributor, README, and TECHNICAL guide.
- Consumers: Mongo and Weaviate discovery/configuration; Ollama's independent AI discovery path; S3's
  current optional client use and transitive full-engine reference.
- Focused tests: Core discovery contributor tests, Mongo ZenGarden initialization specs, Ollama adapter
  specs, registry/generator tests, semantic activation tests, module lifecycle tests, and facts parity.

## Focused discovery and coalescence assessment

### User semantics first

- User's business sentence: “I reference ZenGarden; my already-referenced compatible adapters gain
  one automatic discovery layer. Remove ZenGarden and their autonomous baseline is unchanged.”
- Smallest honest application expression: package references plus the existing `AddKoan()` call. The
  entity and API code, when present, stay byte-for-byte business code.
- Complete action surface:
  - references: the desired adapter and `Sylin.Koan.ZenGarden`;
  - code: `builder.Services.AddKoan();`;
  - decorations: none;
  - configuration: none for automatic behavior; `zen-garden://offering` is the deliberate hard choice;
  - context: none;
  - runtime prerequisite: a reachable offering is required only for ZenGarden to be selected.
- Guarantee: direct engine intent makes one eligible layer available to compatible adapters; normal
  absence, unreadiness, provider failure, or unhealthy automatic candidates safely fall through;
  explicit selection either resolves the named offering or fails before connector I/O.
- Corrective failure: name the adapter, offering, unmet guarantee, and safe choices—make the offering
  ready, reference/enable ZenGarden, or select `auto`/a native connection string. Never expose a
  contributor factory, DI detail, endpoint, credential, tenant value, or raw exception text.
- Additional application concepts: none. Topology reactions such as `ZenGarden.Offering.On(...)`
  remain valid only when the application genuinely expresses topology behavior; they are not setup.

### Current owners and costs

- The legacy ZenGarden auto registrar and public `AddKoanZenGarden()` both register the engine. Manual
  registration runs outside the frozen semantic constitution and is therefore not an equivalent
  activation path.
- Adapter modules register `IZenGardenOfferingBinding` implementations even when ZenGarden is absent.
  `ZenGardenInitializationProvider` rebuilds those identities into a last-write-wins dictionary.
- `ServiceDiscoveryCoordinator` receives `IEnumerable<IDiscoveryCandidateContributor>` and invokes
  every contributor on every discovery operation. Structural composition and live topology lookup are
  conflated.
- `ServiceDiscoveryAdapterBase` correctly owns concrete-over-automatic precedence, normalization,
  health qualification, and fallthrough. It remains the policy owner.
- Mongo, Weaviate, and Ollama each parse explicit ZenGarden intent and currently weaken unresolved
  intent into an autonomous fallback. Their mechanics differ, but their semantic-honesty rule does not.
- Startup reports and runtime discovery facts independently describe declaration, activation, and
  selection rather than projecting one compiled decision set.
- S3 references the full engine for an optional runtime client. R09-02's activation constitution can
  prove that this transitive availability remains inert; Storage topology itself is not service
  discovery and does not belong in this card's first typed target.

### Coalescence and specificity decisions

| Concern | Specificity | Disposition and one owner |
|---|---|---|
| Contribution identity, owner activation, dependency/order validation, deterministic dispatch, and host lifetime | framework | Dispatch closed typed interfaces on R09-02's retained active module instances; reuse their identity, constitution order, decision/problem/evidence, and lifetime instead of creating a second descriptor/factory graph. |
| Discovery-source accumulation | Core service-discovery concern | Add one typed builder and immutable host plan; Core knows sources and service identities, never ZenGarden. |
| Candidate precedence, normalization, health, and auto fallback | adapter/discovery policy | Retain `ServiceDiscoveryAdapterBase`; the generic compiler must not choose providers. |
| ZenGarden topology lookup | optional capability | Split one structural contribution from one live runtime candidate source. |
| Adapter-to-offering identity | discovery concern | Reuse each adapter's canonical service name and aliases; delete ZenGarden-specific binding maps. |
| Explicit `zen-garden://` promise | shared semantic rule, consumer realization | One stable corrective problem contract; each affected consumer performs its typed failure before I/O. Do not force AI or Storage into the service-discovery target. |
| Activation/reporting | host semantic model | Migrate ZenGarden to a generated retained module and project contribution decisions through the existing fact store. |
| S3 topology/replica/credential behavior | Storage | Defer its typed redesign; prove only that a transitive engine reference cannot activate ZenGarden. No temporary ZenGarden-specific Storage seam. |

### Less but more meaningful moving parts

The slice introduces one generic lifecycle and one earned typed target while deleting parallel owners:

- let the already-retained generated module be the structural contributor, avoiding a second
  contributor class, constructor, identity, factory, registry, dependency graph, and ordering law;
- replace public runtime `IDiscoveryCandidateContributor` invocation with a compiled immutable plan;
- replace ZenGarden offering-binding types and its mapping dictionary with existing service identity;
- replace the legacy/manual dual activation path with one generated module and direct reference
  intent;
- preserve dynamic topology as a runtime source rather than re-running structural composition;
- reuse the semantic constitution, decisions/problems/evidence, stable ordering, retained module
  lifecycle, and facts store instead of creating contribution-specific twins; and
- keep AI, Storage, Data, and Communication policy typed rather than inventing a universal candidate
  payload.

### Human, IntelliSense, and coding-model ergonomics

- Application developers and coding agents see only package intent, `AddKoan()`, business entities,
  and deliberate configuration. The automatic path requires no “enable,” “bind,” or “contribute” verb.
- invariant `IContributeTo<TTarget>` is public only so generated modules can implement exact
  typed targets
  across assemblies. It lives in an infrastructure namespace, is hidden from IntelliSense with
  `EditorBrowsable(Never)`, receives no application global using, and stays out of application guidance.
- Startup distinguishes “layer active/eligible” from “provider selected.” It must not imply that an
  active engine won an election.
- Operators and agents receive the same stable, redacted decision identities through startup, HTTP,
  and MCP. Automatic fallback is successful policy execution, not host degradation; explicit failure
  is one corrective rejection.

## Decisions

### DECIDED

- The common path is package references plus `AddKoan()`; `AddKoanZenGarden()` is not an application
  activation contract.
- Direct reference is ZenGarden activation intent. Transitive contracts or engine availability are inert.
- The first generic contract is typed; Core never receives an untyped universal contribution payload.
- A structural contributor is the exact generated `KoanModule` instance already retained for
  registration, startup, and reporting. There is no second contributor descriptor or construction path.
- The constitution filters owners before module construction. Inactive modules can therefore never
  reach a typed contribution target.
- The interface is invariant. A contribution to a base target must not accidentally match a derived
  target or dispatch twice through generic variance.
- Structural contributions compile once per host. Live candidate sources may query current topology
  per discovery but may not discover contributors, enumerate DI collections, reflect, or mutate the plan.
- The service-discovery target accumulates sources. Adapter discovery retains its own election and
  fallback policy.
- Automatic ZenGarden absence/unreadiness/unhealthiness may fall through. Explicit ZenGarden intent
  may not silently weaken.
- Existing adapter service names and aliases replace `IZenGardenOfferingBinding` as the neutral
  discovery identity.
- Startup and all facts projections render canonical compiled decisions. They do not probe or infer
  contribution state independently.
- S3 and Ollama do not enter the first service-discovery target merely for symmetry. Their typed policy
  owners are earned separately.

### DEFAULT

- The existing generator adds each closed target type and a direct apply delegate to its owning module
  descriptor. This is construction-free binding metadata, not a second contributor descriptor,
  identity, factory, dependency graph, or lifecycle. Reflection creates equivalent bindings only in
  the already-degraded manifest fallback.
- A small generic compiler walks the retained active module descriptors in constitution order and
  invokes the exact generated binding for the requested target.
- Each invocation receives an owner-bound ephemeral target facade, so capability authors never repeat
  module identity and Core never needs a universal target base class.
- Target compilers are scheduled during active module registration, drained once after the
  `AddKoan(Action)` declaration callback and before the outer composition session freezes, then removed.
- Contributions stage only in concern-owned builders. The session compiles, validates, and freezes
  every scheduled target before it begins publication. A compilation or freeze failure publishes none;
  any later registration failure faults the service collection and requires a new collection because
  `IServiceCollection` mutations are not transactionally reversible.
- Dynamic source implementations are registered from the frozen plan and constructed once by the host
  provider. Structural module contribution itself has no provider or reflection dependency.

### CLOSED BY RED PROOFS

- Concern-owned targets schedule one compiler during active module registration; the outermost
  `AddKoan()` completion drains all scheduled targets once, after declarations and before freeze.
- Existing component-activation facts remain the declaration/activation truth, one
  `koan.semantic.contribution.applied` fact records the compiled owner/target relationship, and the
  existing service-election fact remains the selected runtime outcome. No projection recomputes them.
- `SemanticDecision.Component` remains unchanged in this bounded slice. Renaming a stable vocabulary
  without a second semantic consumer would create churn rather than coalescence.

## Exact production placement

- `src/Koan.Core/Semantics/Contributions/`
  - hidden `IContributeTo<TTarget>`;
  - one generic dispatcher over retained modules and one immutable compilation result.
- `src/Koan.Core.Registry.Generators/RegistrySourceGenerator.cs`
  - attach exact closed target types and direct apply delegates to generated modules;
  - diagnose contribution interfaces on classes outside that lifecycle.
- `src/Koan.Core/Hosting/Registry/KoanRegistry.cs`,
  `src/Koan.Core/Semantics/SemanticComponentDescriptor.cs`, and `RegistryManifestLoader.cs`
  - carry generated binding metadata in the existing module descriptor ABI and construct it through
    reflection only in the explicitly degraded fallback.
- `src/Koan.Core/Semantics/SemanticModuleRuntime.cs`
  - expose the retained modules to the generic dispatcher in constitution order; never construct a
    contribution-specific instance.
- `src/Koan.Core/Semantics/SemanticCompositionSession.cs` and Core service-collection bootstrap
  - schedule active concern-owned target compilers and drain them once after application declarations,
    before the outer composition freezes.
- `src/Koan.Core/Orchestration/Contributions/`
  - typed service-discovery builder;
  - immutable `ServiceDiscoveryPlan`;
  - runtime `IDiscoveryCandidateSource` contract.
- `src/Koan.Core/Orchestration/ServiceDiscoveryCoordinator.cs`
  - consume the frozen plan; remove per-request contributor enumeration.
- `src/Koan.ZenGarden/Initialization/ZenGardenModule.cs`
  - generated retained registration/start/report lifecycle and implementation of the one typed
    service-discovery contribution interface.
- `src/Koan.ZenGarden/Discovery/`
  - one live ZenGarden source; the retained module owns its structural declaration.
- Mongo/Weaviate/Ollama configuration paths
  - enforce explicit-selection honesty in their existing typed owners.
- Existing Core, Mongo, AI, module, package-consumer, and facts test projects
  - prove the focused matrix without a new broad test aggregate.

## Scope

### In

- Implement the minimal generic typed dispatch lifecycle over retained generated modules.
- Implement the one Core service-discovery typed target and immutable plan.
- Migrate ZenGarden to generated direct-reference activation and the compiled discovery source.
- Remove the redundant application registration path from common guidance and make framework-owned
  registration an internal implementation detail.
- Delete ZenGarden-specific offering bindings where adapter service identities fully replace them.
- Correct explicit unresolved ZenGarden behavior in Mongo, Weaviate, and Ollama without conflating
  their typed runtime policies.
- Project active/eligible/selected/fallback/rejected truth safely through the existing startup/facts
  surfaces.
- Prove S3's transitive engine availability is inert; record its separate Storage target as a later
  bounded migration rather than adding a temporary abstraction.
- Repair documentation that teaches duplicate registration, silent explicit fallback, nonexistent
  APIs/keys, or unsupported S3 behavior.

### Out

- Shared Data/Communication provider-selection machinery; that is the next backlog outcome.
- A universal contribution DSL, public application contributor syntax, or an Entity contribution facet.
- A Storage topology/replica/credential target or an AI discovery target.
- General Tenancy segmentation, hard-overlay coverage, or the V1.1 neutral operation model.
- Broad contributor/module conformance, trim/AOT certification, or release certification.
- Public publication, push, tag, release, remote mutation, or private downstream inspection.

## Business-code proof

Automatic layering adds no application code:

```csharp
builder.Services.AddKoan();

public sealed class Todo : Entity<Todo> { }
public sealed class TodoController : EntityController<Todo> { }
```

The project references its adapter and `Sylin.Koan.ZenGarden`. With a reachable compatible offering,
Koan selects it. Without ZenGarden—or when its automatic candidate is unavailable—the same program
uses the adapter's autonomous baseline.

A deliberate hard choice remains configuration because it expresses a real guarantee:

```json
{
  "Koan": {
    "Data": {
      "Mongo": {
        "ConnectionString": "zen-garden://mongodb"
      }
    }
  }
}
```

That configuration either resolves the named offering or yields one corrective failure. It does not
fall back. No scaffolding or framework-maintenance concept appears in the application.

## Execution plan

1. Add focused red fixtures for generated retained-module bindings, owner activation,
   validation-before-effects, deterministic order, per-host construction, and
   source/package/transitive behavior.
2. Extend the existing module descriptor ABI with exact target bindings, then implement the minimal
   typed dispatcher and composition-session target drain by reusing R09-02's retained module runtime,
   constitution order, decision/problem/evidence, and failure boundary.
3. Add the typed service-discovery builder/plan/source contract; make the coordinator execute the
   immutable plan and delete its runtime contributor chain.
4. Migrate ZenGarden to one generated retained module that declares one structural typed source,
   plus the separately DI-created dynamic source. Remove manual/common-path activation and duplicated
   offering bindings.
5. Reverse explicit-fallback tests and implement safe corrective failure in Mongo, Weaviate, and Ollama
   at their existing typed chokepoints.
6. Project canonical contribution decisions through startup/facts, retaining final adapter election as
   a separate runtime truth and proving redaction.
7. Prove removal, S3 transitive inertia, repeated discovery economy, and two-host isolation; delete
   superseded code and correct public/project documentation.
8. Run only the focused matrix, direct changed-project builds, docs/link/diff/privacy checks, then record
   the next shared-selection restart point.

## Red proof matrix

| Cell | Required observation |
|---|---|
| Inactive owner | descriptor is available but its module factory/constructor/contribution/source counts remain zero |
| Direct source/package intent | ZenGarden activates and contributes exactly once through `AddKoan()` with no manual registration |
| Transitive availability | S3/full-engine or contract-only presence leaves ZenGarden inactive and untouched |
| Retained identity | the one module instance registers, contributes, starts, and reports; no contributor-specific instance exists |
| Invalid graph | existing module graph failures and target source/scheme collisions reject before plan/source registration |
| Stable order | reversed availability input still follows constitution owner order, then stable target source identity |
| Cross-target atomicity | target B rejection publishes neither target A nor target B plan/compilation snapshot |
| Two hosts | plans, modules, and dynamic source instances are distinct; no process-static runtime state leaks |
| Dynamic discovery | repeated calls may query topology/health but do not recompile, enumerate contributor DI, reflect, or mutate the target |
| Active + reachable | ZenGarden is eligible and its healthy candidate can be selected |
| Automatic unavailable | absent, unready, throwing, or unhealthy source falls through and reports the actual selected method without degrading readiness |
| Explicit unavailable | Mongo, Weaviate, and Ollama reject before connector I/O with one safe correction; no autonomous fallback |
| Removal | no engine reference produces the autonomous plan with no leftover services, facts, or static state |
| Projection | startup, HTTP facts, and MCP facts agree on stable active/eligible/selected identities and contain no endpoint/credential/exception text |
| Retained report | the exact generated ZenGarden module instance used for registration/start supplies its unique report once |

## Verification economy

- Core generated-binding, retained-module contribution/compiler/session focused tests plus the existing
  descriptor package fixture.
- Core service-discovery plan/election tests.
- Real `AddKoan()` ZenGarden activation fixture in source form and one staged package consumer.
- Mongo automatic/explicit/removal tests; bounded Weaviate and Ollama explicit-intent tests.
- One S3 transitive-inertia fixture, not a Storage redesign suite.
- ZenGarden module lifecycle and startup/HTTP/MCP fact parity tests.
- Builds only for directly changed projects and their focused consumers.
- Documentation links, `git diff --check`, privacy sweep, and worktree review.
- No release-certification aggregate. Broader tests require a focused failure with a specific risk.

## Deletion targets

- public runtime `IDiscoveryCandidateContributor` and per-request contributor application;
- `DiscoveryContext.ContributedCandidates` as an external/twin decision path;
- current ZenGarden discovery contributor after its dynamic-source rebuild;
- `IZenGardenOfferingBinding`, binding implementations/registrations, and last-write-wins binding map
  where existing service name/aliases are sufficient;
- redundant common-path `AddKoanZenGarden()` activation and documentation;
- explicit-silent-fallback branches and tests;
- re-probing/reconstruction used only to report compiled ZenGarden state; and
- unused or contradictory ZenGarden configuration constants and documentation.

## Acceptance additions

- One package reference produces one useful optional behavior change; removing it restores baseline.
- `AddKoan()` remains the sole application bootstrap and contributor mechanics remain invisible to
  application developers, IntelliSense, and application-authoring agents.
- Every new public ABI exists only for cross-assembly module authoring, is hidden from the
  common discovery surface, and has its first real consumer in this card.
- Automatic fallback and explicit rejection are both truthful and separately proved.
- Structural composition happens once per host; runtime topology remains live without recomposition.
- One canonical decision set feeds startup, HTTP, and MCP while final runtime selection remains a
  distinct fact, not an inferred activation claim.
- The implementation has fewer decision owners at close: no manual/semantic activation twin, no
  binding identity twin, and no per-request structural contributor chain.
- Documentation teaches intent and guarantees before internal mechanism and contains no unsupported
  fallback or Storage claims.

## Stop conditions

- Stop if the common path requires `AddKoanZenGarden()`, a contributor call, binding, attribute, or
  adapter glue.
- Stop if Core names ZenGarden, contains a pillar switch, or accepts an untyped universal payload.
- Stop if typed dispatch constructs a second object instead of using the retained active module, or if
  invalid target state can register a plan/source before rejection.
- Stop if normal automatic fallback is reported as host degradation or explicit intent can weaken.
- Stop if structural contributors run per request, tenant, entity, message, or discovery operation.
- Stop if startup/facts independently reconstruct contribution decisions or disclose sensitive values.
- Stop if symmetry pulls S3 or Ollama policy into the service-discovery target without matching meaning.
- Stop if the slice leaves old and new activation, binding, contribution, or fact owners canonical.
- Stop before broad certification, publication, push, tag, release, external mutation, or private
  downstream inspection.

## Kickoff record

- Date: 2026-07-16.
- Starting branch/HEAD: `dev` at `546817ee0d3a`.
- Exploration disposition: package intent plus `AddKoan()` is the complete common path; one generic
  typed lifecycle is earned by one Core service-discovery target and one useful ZenGarden vertical.
- Coalescence disposition: retain adapter election/health policy and live topology; rebuild structural
  composition and engine activation; absorb adapter identity into existing service names/aliases;
  delete duplicate bindings, manual activation, and per-request contributor discovery.
- Repository boundary: preserve all pre-existing R08/R09 and packaging changes; never stage `tmp/`;
  never inspect or name private downstream applications.
- First implementation action: add the focused retained-module dispatch/session red proofs before
  editing production types.

## Closure record

- Date: 2026-07-16.
- Result: `passed` with the common path reduced to package intent plus `AddKoan()`.
- Generic foundation: one hidden invariant typed contract dispatches over the exact retained active
  module instances in constitution order; generated bindings are the normal path and reflection is
  retained only for the already-degraded manifest fallback.
- Useful vertical: Core compiles one immutable service-discovery source plan; ZenGarden contributes one
  live source from its generated module; adapter-owned precedence, health, normalization, and
  automatic fallback remain unchanged.
- Semantic honesty: Mongo, Weaviate, and Ollama explicit ZenGarden intent now resolves through its
  required path or throws one corrective failure. It never invokes autonomous fallback.
- Deleted owners: the runtime discovery-contributor chain, `DiscoveryContext` candidate injection,
  ZenGarden offering bindings and their implementations/map, duplicate connector bindings, and the
  public/manual `AddKoanZenGarden()` activation path.
- Focused evidence: Core contribution lifecycle 9/9; Core discovery plan 15/15; ZenGarden source and
  provider 12/12; Mongo 3/3; Ollama 5/5; Weaviate explicit source probe 1/1; staged Core package
  generator probe 1/1; S3 transitive-inertia probe 1/1; HTTP fact-envelope projection 1/1; MCP
  fact-envelope projection 1/1. The changed generator and Weaviate projects build cleanly; existing
  unrelated Mongo XML-doc and Web unreachable-code warnings remain outside this slice.
- Projection evidence: the compilation snapshot adds its redacted fact to the canonical host fact
  store; the established HTTP and MCP resources serialize that exact store rather than reconstructing
  contribution state.
- Economy: no solution aggregate, release-certification suite, publication, push, tag, release, remote
  mutation, or private downstream inspection occurred.
- Next outcome: assess shared deterministic Data/Communication selection mechanics from their current
  typed policy owners before creating R09-04's implementation card.
