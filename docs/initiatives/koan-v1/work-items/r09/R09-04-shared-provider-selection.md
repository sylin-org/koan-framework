---
type: SPEC
domain: framework
title: "R09-04 - Compile Shared Provider Selection Mechanics"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-16
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-16
  status: passed
  scope: focused Core catalog, Data/Vector, Communication, Cache, package-intent, lock, HTTP, and MCP evidence
---

# R09-04 — Compile shared provider selection mechanics

- Tranche: `T7A — semantic composition prerequisite`
- Status: `passed`
- Depends on: R09-03 typed contribution lifecycle and compiled optional-layer proof
- Unlocks: hard Tenancy overlays against stable, provider-qualified pillar plans
- Owner: Core provider-catalog mechanics; Data, Communication, and Cache typed selection policy

## Meaningful outcome

An application references Koan and the provider it means to use. With no further ceremony, direct
provider intent wins over a bundle's safe floor. A deliberate provider name is honored exactly or
fails with one correction. Reordering DI registrations, adding an unrelated transitive adapter, or
running a second host cannot change the result.

The common application remains business code:

```csharp
builder.Services.AddKoan();

public sealed class Todo : Entity<Todo> { }
public sealed class TodoController : EntityController<Todo> { }
```

The project references the desired Data or Communication connector. Configuration, an Entity
decoration, or an ambient Data override appears only when the application is making a real routing or
deployment decision. Provider catalogs, candidates, priorities, contribution targets, and receipts do
not appear in application code or application-authoring agent guidance.

## User semantics first

### Business sentences

- Data: “Use the persistence connector I referenced. If I explicitly name an adapter or source, use
  exactly that choice.”
- Communication: “Keep Events and Transport local with no connector. When I directly reference or pin
  a connector, use it only where it preserves that lane's contract; never fall back silently.”
- Cache corroboration: “Choose one provider per local/remote tier deterministically; an explicit tier
  pin is exact.”

### Complete action surface

| Surface | Common path | Deliberate override |
|---|---|---|
| References | Koan bundle plus the desired connector | additional providers may be referenced for genuinely distinct roles |
| Code | `AddKoan()` plus ordinary Entity/API business code | none |
| Decorations | none | `[SourceAdapter]`, `[DataAdapter]`, or `[VectorAdapter]` expresses Entity-level provider intent |
| Configuration | none | named Data sources/defaults, Communication channel/provider pins, Cache tier pins |
| Context | none | `EntityContext.Source` / `Adapter` expresses operation-flow Data routing |
| Runtime prerequisite | the selected provider must realize its documented readiness and operation guarantees | an exact override must be referenced/available and eligible |

### Guarantees and corrections

- A direct Data connector outranks a bundle floor; a transitive dependency cannot become persistence
  merely because its factory is present.
- Required intent—context adapter/source, configured source/default, Entity adapter decoration,
  vector-specific decoration/default, Communication pin, or Cache pin—never weakens to an unrelated
  provider.
- Advisory cross-role inference, such as pairing a vector provider from the record provider name, may
  fall through only when its origin is explicitly classified as preferred rather than required.
- Duplicate provider IDs or aliases reject before an operation chooses an arbitrary first registration.
- Every successful choice has a stable subject, provider ID, intent posture, priority, and reason.
- Corrective failures name the business choice, eligible alternatives where safe, and the exact place
  to change the reference/configuration. They contain no connection value, credential, tenant value,
  endpoint, DI registration order, or raw exception.

## Focused discovery and coalescence assessment

### Current owners and repeated mechanics

| Current owner | Behavior and cost | Disposition |
|---|---|---|
| `AdapterResolver` | Computes Data precedence, but returns strings; `DataService` then enumerates factories again to recover the selected object | **Rebuild** as one typed Data route decision containing the selected provider handle |
| `FactoryResolver` | Reflects `[ProviderPriority]`, ranks by implementation type name, and silently falls back when a desired provider has no match | **Delete** after catalog migration; exact and advisory intent become distinct |
| `DataService` / `AdapterNaming` | Re-resolve routes and enumerate `IDataAdapterFactory`; naming adds a process-static weak-table lookup cache | **Absorb** into one host-owned Data provider runtime and route cache |
| `AggregateConfigs`, `VectorConfigs`, `VectorService`, Web helper | Carry independent copies or consumers of provider-name derivation, reflection priority, and fallback | **Rebuild/absorb** onto the Data/Vector provider catalogs; delete copied ranking |
| `CommunicationRouter` | Already compiles a strong immutable route plan, but owns its own ID collision, direct-reference matching, priority reflection, and stable tie mechanics | **Keep typed policy; absorb** only identical catalog mechanics |
| `CacheTopologyResolver` | Repeats exact-pin lookup, reflection priority, and stable-name tie for Local/Remote tiers | **Migrate as corroborating proof**; retain Cache placement/topology semantics |
| Data composition contributor | Re-runs default Data selection to report it | **Delete as decision owner**; project the canonical Data plan/receipt |
| Communication composition contributor | Reads the router's existing decisions | **Keep as temporary projection**, then narrow it to canonical receipts; it must never re-elect |

### Exact common mechanics

Core may own one immutable, host-owned generic provider catalog with:

1. stable case-insensitive provider ID normalization while preserving the canonical display ID;
2. explicit aliases and direct-reference identities;
3. empty/invalid ID rejection and host-wide ID/alias collision rejection within one typed catalog;
4. one-time priority extraction and immutable candidate ordering;
5. exact ID/alias lookup;
6. source/package direct-reference matching through `KoanApplicationReferenceManifest`;
7. deterministic best-candidate selection using a pillar-supplied comparer followed by stable ID; and
8. a bounded selection receipt carrying safe identity, reason, intent posture, priority, and rejected
   reason codes without pillar-specific prose or sensitive values.

The catalog stores typed providers or typed lazy handles. It does not create a universal provider
interface, capability enum, target key, fallback rule, configuration grammar, or runtime operation.

### Typed policies that must not move into Core

| Pillar | Typed policy retained by the pillar |
|---|---|
| Data | source/database-axis/context/Entity/default precedence; record versus vector role; required versus preferred cross-role intent; source and repository construction; operation capability failure |
| Communication | lane/channel keys; built-in/layered/direct candidate stages; hard copy/context/group/fan-out/acceptance requirements; assurance ranking; binding construction; startup/readiness and no-local-fallback law |
| Cache | Local versus Remote placement, optional tier absence, coherence activation, and resulting topology |

The pillar supplies the candidate subset, qualifier, ordered score/comparer, selected reason, and
corrective problem. Core appends only the stable identity tie-break and records the neutral receipt.

### Intent postures

The assessment recognizes three internal postures, because current code conflates them:

- `required`: a named choice must match and qualify or reject;
- `preferred`: try the named/correlated provider, then continue through the pillar's documented policy;
- `automatic`: choose from the pillar-approved direct/layered/floor set.

These are framework vocabulary for evidence, not public application syntax. Data classifies explicit
context/configuration/decorations as required; vector inference from a record-provider name is
preferred; reference/default selection is automatic. Communication pins are required, direct-reference
sets are mandatory automatic intent, and the in-process floor is automatic only when no external
intent exists. Cache pins are required and unpinned tier selection is automatic.

### Candidate activation and transitive safety

- Known build provenance: Data automatic selection considers direct connector candidates plus the
  bundle's declared safe floor. A transitive provider dependency remains explicit-only unless a typed
  capability contribution makes it an automatic layered candidate.
- Unknown provenance in deliberate low-level/manual hosts: preserve a deterministic priority fallback
  and mark the evidence as degraded/unknown rather than inventing direct intent.
- Communication retains its built-in floor and typed layered candidates. Merely registering an external
  adapter does not make it automatic.
- Provider registration may remain DI-backed in this slice, but the catalog compiles once per host and
  ordinary operations never enumerate DI or reflect. Descriptor-only preconstruction filtering remains
  an R09 conformance/economy concern unless this implementation can delete it without adding a second
  descriptor owner.

## Target architecture

### Framework law

- Add one hidden cross-assembly generic catalog/receipt substrate under `Koan.Core/Providers` (exact
  names earned by red tests).
- Reuse `ProviderPriorityAttribute`, `KoanApplicationReferenceManifest`, `SemanticId` normalization law,
  and the canonical fact store; do not copy them.
- Compile once per host. Catalog construction validates completely before publication.
- The catalog never resolves services, starts a provider, probes health, or interprets capabilities.

### Data realization

- Add one host singleton Data provider runtime with immutable record and vector catalogs.
- Rebuild route resolution to return one typed decision containing provider handle, provider ID, source,
  posture, reason, priority, and safe evidence.
- Compile static Entity decorations once; bind ambient source/adapter/database-axis values per operation.
- Reuse the decision for repository construction, naming, diagnostics, facts, and resolved lock output.
- Replace `CanHandle`'s opaque code with declarative aliases at the factory contract; connector-specific
  configuration uses the same alias declaration. Provider IDs, aliases, and reference identities have
  one owner.
- Required Data/vector intent fails at the route boundary. Preferred record-to-vector pairing may
  continue to the vector automatic set and records that fallthrough.

### Communication realization

- Build one generic catalog from Communication descriptors, then retain the existing typed per-lane and
  per-channel route compiler.
- Replace router-local duplicate/ID/direct/priority/tie code with catalog operations.
- Keep hard capability filtering, assurance ordering, built-in/layered activation, bindings, adapter
  startup, readiness, and publication semantics in Communication.
- Do not change `entity.Events`, `entity.Transport`, business channels, or receipt semantics.

### Cache corroboration

- Use the same catalog for store identity, exact pin, priority, and stable tie.
- Keep placement filtering and optional single-tier outcomes entirely in Cache.
- Do not pull Tenancy segmentation into this card; R09-05 consumes the stable topology afterward.

## Human, IntelliSense, and coding-model ergonomics

- No new application API, service locator, provider builder, or contributor concept.
- A coding agent can infer common behavior from references and existing business-facing decorations or
  configuration; it does not need to inspect registration order or factory class names.
- Errors distinguish “required provider unavailable,” “provider present but ineligible,” and “no
  automatic provider,” then give one bounded correction.
- Startup, HTTP facts, MCP facts, and the resolved lock consume the same receipts. A reviewer can see
  why a direct provider displaced a floor and why another candidate was rejected.
- Provider extension authors receive one declarative identity/alias/reference contract and focused
  conformance tests. Infrastructure ABI required across assemblies remains hidden from normal
  IntelliSense and is not imported globally.

## Exact implementation/deletion placement

- `src/Koan.Core/Providers/`
  - immutable generic candidate catalog, candidate descriptor/handle, intent posture, and safe receipt;
  - one cached priority reader and direct-reference matcher.
- `src/Koan.Data.Abstractions/IAdapterFactory.cs`
  - replace opaque matching as the canonical identity source with declarative aliases/reference identity.
- `src/Koan.Data.Core/`
  - one Data provider runtime and typed route decision/cache;
  - rebuild `AdapterResolver` call sites or replace it entirely.
- `src/Koan.Data.Vector/` and `src/Koan.Data.Vector.Abstractions/`
  - consume the vector catalog and distinguish required vector intent from preferred role pairing.
- `src/Koan.Communication/Runtime/CommunicationRouter.cs`
  - retain typed route policy; delegate common catalog mechanics.
- `src/Koan.Cache/Topology/CacheTopologyResolver.cs`
  - retain tier policy; delegate common catalog mechanics.
- existing Core, Data, Vector, Communication, Cache, package-consumer, facts, and lock tests
  - prove the bounded matrix without a release aggregate.

Delete before pass:

- `FactoryResolver` and every copied type-name/priority ranking path;
- desired-provider silent fallback for required Data/vector intent;
- `DataService`'s second factory enumeration after route resolution;
- process-static `AdapterNaming` factory lookup state;
- router-local provider ID collision, direct-reference matching, priority reflection, and final tie code;
- Cache's copied pin/rank/tie mechanics;
- Data facts/lock selection reconstruction; and
- provider-name derivation from `*AdapterFactory` implementation class names.

## Red proof matrix

| Cell | Required observation |
|---|---|
| Catalog determinism | reversed candidate/DI order yields the same canonical catalog and selection |
| Identity honesty | empty ID, duplicate ID, duplicate alias, or ID/alias collision rejects before publication |
| Exact intent | required ID/alias miss or ineligible match rejects and never selects the highest-ranked unrelated provider |
| Preferred intent | an unavailable cross-role hint may continue only through an explicitly preferred stage and records that reason |
| Stable ranking | pillar comparer wins; equal scores use canonical provider ID, never registration/type-name order |
| Direct evidence | source/package identities select the same provider; transitive dependency presence is not automatic intent |
| Unknown provenance | low-level host fallback is deterministic and explicitly marked degraded/unknown |
| Data common path | foundation plus one direct connector and only `AddKoan()` selects the direct connector |
| Data explicit paths | context source/database axis/context adapter/Entity/default each either select exactly or reject correctively |
| Vector honesty | explicit vector decoration/default never falls through; preferred record-role pairing may |
| Data economy | repeated known repository/naming operations do not enumerate factories, reflect priority, or re-elect |
| Communication preservation | local floor, direct connector, explicit pin, lane capabilities, assurance order, layered candidate, and no-fallback cells remain green |
| Communication counterexample | a lower-priority higher-assurance provider can beat a higher-priority lower-assurance one, proving rank remains typed |
| Cache corroboration | Local/Remote placement remains typed while exact pin and deterministic tie use the shared catalog |
| Two hosts | different references/options compile different catalogs/decisions with no static leakage |
| Facts/lock | startup, HTTP, MCP, and resolved lock project the selected receipt rather than invoking a selector |
| Privacy | receipts/errors contain no endpoint, connection string, credentials, tenant value, or raw exception |

## Verification economy

- Core provider-catalog focused tests.
- Data provider routing/catalog and vector-resolution focused tests.
- Communication provider-election/channel/layered-candidate focused tests.
- Cache topology resolver focused tests.
- One source and one staged-package application proof for direct Data intent versus bundle floor and
  transitive dependency inertia.
- Exact HTTP/MCP fact-envelope and resolved-lock cells only when their canonical input changes.
- Directly changed project builds/packs, docs/examples/links, `git diff --check`, removal and privacy sweeps.
- No solution aggregate or release-certification suite. Broader runs require a specific focused failure
  or R08 resumption.

## Delivered architecture

- `Koan.Core/Providers` now owns one immutable typed catalog for canonical IDs, aliases, direct package
  evidence, collision rejection, cached priority, exact lookup, deterministic final tie, and bounded
  `ProviderSelectionReceipt` evidence. It owns no pillar eligibility or fallback policy.
- Data compiles one host-owned record-provider catalog and one lazy canonical default plan. Runtime
  repositories, aggregate metadata, health participation, vector role correlation, composition facts,
  and the resolved lock consume that decision instead of reconstructing it.
- `IAdapterFactory.CanHandle`, implementation-class-name provider derivation, process-static naming
  lookup state, and `FactoryResolver` are deleted. Provider IDs and aliases are declarative and shared
  with connection-source ownership.
- Communication compiles one catalog per host and reuses it across routes, while retaining lane
  capabilities, assurance ordering, layered/built-in admission, bindings, readiness, and no-fallback
  semantics in its typed router.
- Cache delegates identity, exact pin, priority, and stable tie to the shared catalog while retaining
  Local/Remote placement and optional tiers. Its registry now distinguishes idempotent registration
  from a conflicting duplicate identity instead of silently discarding the collision.
- Startup composition, HTTP facts, MCP facts, and the resolved lock project canonical receipts. The
  receipt vocabulary is semantic and bounded; endpoints, connection values, credentials, tenant
  values, registration order, and raw exceptions are excluded by contract.
- A staged-package proof demonstrates the user promise: `AddKoan()` plus a directly referenced Data
  connector selects that connector even when it has lower priority than a transitive connector; the
  transitive connector remains inert. No provider-catalog concept enters application code.

## Closure evidence

- Core provider catalog: 14/14 focused tests.
- Data catalog, naming, host ownership, and vector correlation: 20/20 focused tests; the broader named
  Data provider/routing cells also passed 16/16 and 33/33 during migration.
- Communication provider, channels, local Events/Transport, assurance counterexample, and aliases:
  11/11 focused tests.
- Cache selection, placement, receipts, and registry collision behavior: 9/9 focused resolver tests;
  the canonical cache receipt projection passed separately.
- Real staged-package direct-versus-transitive intent: 2/2. Canonical resolved-lock decisions: 2/2.
- Exact HTTP and MCP fact envelopes: 1/1 each. Degradable Web aggregate reporting: 1/1.
- All directly affected connector projects compiled through focused builds/tests. Existing unrelated
  XML `cref` warnings in Qdrant and Mongo, plus existing Web/Tenancy warnings, were not widened into
  this slice.
- Removal sweep found only Core's canonical cached priority reader; `FactoryResolver` is absent.
  Privacy and diff-hygiene sweeps are clean. Focused documentation lint reports zero errors and 28
  pre-existing warnings in the scanned directories, including the already-front-matter-free changed
  guides.
- No aggregate solution, release-certification, publication, push, tag, or remote mutation was run.

## Closure decision

R09-04 passes. The shared mechanism is materially smaller in ownership without pretending the pillars
share meaning: Core owns the identical mechanics once; Data, Communication, and Cache each retain one
typed policy chokepoint. R09-05 may now compile Tenancy's single semantic dimension into those stable
provider-qualified pillar plans, beginning with Data, Cache, and Storage.

## Scope

### In

- One generic catalog/receipt substrate whose mechanics are identical across providers.
- Data record/vector, Communication, and Cache migrations described above.
- Direct/bundle/transitive candidate truth and exact required intent.
- One-time host compilation, stable selection facts, and deletion of copied selectors.
- Documentation of common-path behavior, deliberate overrides, corrections, and unsupported scenarios.

### Out

- One universal provider interface, capability enum, selection policy, target key, or fallback law.
- Tenancy segmentation, provider operation plans, Storage selection, Jobs ledger selection, or AI routing.
- Provider failover, retry, mirroring, weighted routing, dynamic channels, or runtime health re-election.
- Descriptor-only pre-instantiation/AOT conformance if it requires a second provider descriptor owner;
  R09's conformance slice owns that final economy proof.
- V1.1 neutral operation model, public release work, publication, push, tag, release, or remote mutation.

## Stop conditions

- Stop if normal application code must name the catalog, selection engine, priority, or candidate.
- Stop if Core must name a Data source, Communication lane, Cache tier, adapter capability, or pillar correction.
- Stop if required intent can fall through, or if direct external Communication intent can become local.
- Stop if the common catalog owns assurance, placement, source precedence, eligibility, or fallback.
- Stop if a provider ID or alias has two canonical owners.
- Stop if ordinary operations enumerate DI candidates, reflect priority, mutate the catalog, or recompute facts.
- Stop if compatibility leaves the copied selectors as alternate authorities.
- Stop before broad certification, external mutation, or private downstream inspection.

## Kickoff record

- Date: 2026-07-16.
- Starting branch/HEAD: `dev` at `546817ee0d3a` with the intentional R08/R09 working tree preserved.
- Exploration disposition: one immutable provider catalog is the narrowest framework-wide common owner;
  Data, Communication, and Cache selection policy remains typed.
- Coalescence disposition: rebuild Data's string-then-enumerate resolution, absorb Communication/Cache's
  identical catalog mechanics, delete class-name ranking and required-intent fallback, and project one
  canonical receipt.
- First production action: write Core catalog red proofs for collisions, exact intent, direct evidence,
  caller-owned ordering, two-host isolation, and no repeated reflection before adding production types.
