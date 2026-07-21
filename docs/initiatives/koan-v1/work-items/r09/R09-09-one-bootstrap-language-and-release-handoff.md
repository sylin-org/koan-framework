---
type: SPEC
domain: framework
title: "R09-09 - One Bootstrap Language and Release Handoff"
audience: [architects, maintainers, developers, ai-agents]
status: accepted
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: repository-wide single-module migration, legacy kernel deletion, contract-boundary proof, current-sample convergence, and focused source/package journeys
---

# R09-09 — One bootstrap language and release handoff

- Tranche: `T7A — semantic composition prerequisite`
- Status: `passed`
- Depends on: R09-01 through R09-08
- Unlocks: R09 parent closure and responsible R08 resume
- Owner: Core semantic constitution; each implementation assembly owns one concrete module

## Meaningful outcome

Koan has one bootstrap language:

```csharp
public sealed class MyCapabilityModule : KoanModule
{
    public override void Register(IServiceCollection services)
        => services.AddMyCapability();
}
```

Application developers still write only `AddKoan()`. Module authors derive one ordinary class and override
only the lifecycle verbs their concern needs. There is no second initializer/auto-registrar interface,
registry, construction path, provenance path, or compatibility bridge to learn or accidentally invoke.

FirstUse and GoldenJourney keep the same application body while references add capabilities. Startup,
facts, health, HTTP/MCP inspection, and the lock model continue to project the one compiled constitution.

## Focused discovery and coalescence assessment

**User's business sentence:** “Reference a capability and let `AddKoan()` compose it once.”

**Smallest honest application expression:** reference plus `AddKoan()`; no application bootstrap code.

R09-08 completed the target module lifecycle, but the old bootstrap remains materially active:

- 74 concrete legacy registrar files across 68 production projects still implement
  `IKoanAutoRegistrar`/`IKoanInitializer`;
- the generator emits initializer and auto-registrar type tables beside semantic descriptors;
- `KoanRegistry`, `RegistryManifestLoader`, `AppBootstrapper`, and `AppRuntime` retain separate legacy
  registries, reflection construction, ordering, activation facts, and provenance reporting;
- `KoanModule` implements the old interfaces through an explicit compatibility bridge; and
- several assemblies contain two registrars, proving that the old type-level model does not express the
  desired one-assembly/one-capability owner.

This is distributed complexity N with multiple points to touch. The correct coalescence is not a generated
wrapper or another adapter layer. Each functional assembly gets one concrete module; multiple registrars in
one assembly merge into that owner. Core-always-present registrars become direct Core registration. Contracts
assemblies remain module-free.

## Responsibility boundaries

- **Core constitution:** construction-free discovery, activation, ordering, retained instance, lifecycle,
  corrective failure, and canonical activation evidence.
- **Implementation assembly:** one module and its concern-owned registration/start/report behavior.
- **Contracts assembly:** vocabulary only; no module and no functional dependency.
- **Pillar/adapter:** typed configuration, provider policy, health, and operational guarantees.
- **Application:** references, `AddKoan()`, and business declarations only.

`[Before]`/`[After]` remain the exceptional type-safe ordering language. Standard `PackageId`, assembly
references, DI, options, hosted services, health, and logging remain the substrate. No new Koan ID,
descriptor, activation attribute, or project metadata is admitted.

## Migration tranches

1. **Foundation journey:** Web, MCP, SQLite/JSON/relational, Core adapters, and their direct runtime
   dependencies. Coalesce duplicate MCP registrars. Prove unchanged FirstUse application code.
2. **Semantic pillars:** Cache, Communication connectors, Jobs adapters, Media Web, Storage/connectors,
   Tenancy Web, and data/vector connectors. Preserve typed plans and focused facts.
3. **Application capabilities:** Identity/Auth, OpenAPI/Admin/SSE/backup, AI/model/eval/training/review,
   orchestration/observability, and other active packages. Coalesce every duplicate assembly owner.
4. **Disposition obsolete families:** delete unsupported pre-V1 Messaging or other superseded packages when
   no current public journey/owner justifies migration; otherwise migrate them explicitly. No aliases.
5. **Kernel deletion:** remove both legacy interfaces, generator tables, registry sets, reflection fallback,
   bootstrap loops, provenance reconstruction, compatibility bridge, and legacy-only ordering language.
6. **Release handoff:** run focused clean source/package FirstUse and GoldenJourney, documentation/source
   ratchets, meaningful-step scorecard, then update R09/R08 state. Do not publish.

## Per-tranche gate

- Every functional assembly has exactly one concrete `KoanModule`; every contracts assembly has none.
- The module uses derived identity and ordinary references only.
- Registration/start/report output remains concern-owned and observable.
- Source-generated and degraded fallback paths agree without constructing inactive modules.
- Named owner/consumer tests pass; no release certification runs during migration.
- The legacy implementation count decreases monotonically; no bridge or compatibility alias is added.

## Implementation record

### Foundation journey — passed

- JSON, SQLite, Core adapters, relational data, Web SSE, Web, and MCP now expose domain-named modules.
- Web's transformer registrar and MCP's diagnostics registrar were coalesced into their assembly's single owner.
- Core's always-present discovery and background-service concerns became internal bootstraps called directly by
  `AddKoanCore()`; they are no longer lifecycle objects.
- FirstUse application code remained unchanged and its focused source contract passed.

### Semantic pillars, tranche A — passed

- Cache owns its process-memory floor in the single `CacheModule`; the empty memory registrar was deleted and
  its useful capacity evidence moved into the pillar report.
- Redis/SQLite cache adapters, RabbitMQ Communication, Media Web, Storage, Local Storage, and S3 Storage now
  expose one domain-named module each. `MediaCoreModule` was renamed from its legacy-shaped class name.
- All nine affected projects build cleanly.
- Focused proof passed: Cache composition (1), Media startup (2), Storage failure/bootstrap (4), and RabbitMQ
  topology (1). Existing unrelated XML/unreachable-code warnings were observed only in transitive projects.
- No wrapper, alias, activation metadata, or inert reference was added.

### Semantic pillars, tranche B — passed

- The Vector Data pillar and its InMemory, Milvus, Qdrant, SQLite-vec, and Weaviate adapters now each expose
  one domain-named module. The pillar's registrar-only debug chatter was removed; the semantic lifecycle is
  already the single observable activation owner.
- All six projects build; focused vector bootstrap and explicit ZenGarden/Weaviate discovery behavior passed.
- The remaining concrete legacy implementation inventory is 48 files across 46 projects, down monotonically
  from the assessed 74 files across 68 projects. The only observed build warning is Qdrant's pre-existing XML
  documentation warning.

### Semantic pillars, tranche C — passed

- Cockroach, Couchbase, Elasticsearch, InMemory, OpenSearch, Postgres, Redis, and SQL Server data adapters now
  expose one domain-named module each. Focused `AddKoan()` InMemory/data-pillar behavior passed.
- Aspire resource ownership was found to depend on the literal class name `KoanAutoRegistrar`. That parallel
  convention was removed: the assembly's one module optionally implements `IKoanAspireResources`, and discovery
  follows that typed capability with a corrective multiple-owner check. Postgres and Redis retain their Aspire
  behavior on the same module; the Aspire package itself now exposes `AspireModule`.
- All affected data and Aspire projects build cleanly. Active Aspire package docs now teach the single-module
  model instead of the registrar pattern.
- The remaining concrete legacy implementation inventory is 39 files across 37 projects. No legacy alias was
  retained for the renamed Aspire contract.

### Application capabilities, Web tranche — passed

- Admin's two lifecycle owners coalesced into `AdminModule`; its core and web-specific registration/reporting
  remain separate internal concerns, not separate bootstrap objects.
- OpenAPI and Swagger's two lifecycle owners coalesced into `OpenApiModule` with an internal Swagger concern.
  The module now reports both the document and UI from one activation identity.
- Web Extensions, OpenGraph, Web Backup, Auth, Auth Roles, Auth Services, Canon Web, and the Discord, Google,
  Microsoft, OIDC, and Test auth providers now expose domain-named modules. Type-safe Auth ordering targets the
  new `AuthModule` directly.
- All affected projects build. Focused Auth/Canon startup (2), OpenGraph ordering (1), and Swagger UI gates (5)
  passed. Pre-existing XML/unreachable-code warnings remain outside this slice.
- Admin's own UI now describes compiled module activation instead of the removed registrar mechanism.
- The remaining concrete legacy implementation inventory is 23 files across 23 projects.

### Application capabilities, AI tranche — passed

- AI core, Agents, Compute, Evaluation, Models, Orchestration, Review, Training, Data AI, and the Hugging Face,
  LM Studio, Ollama, and ONNX connectors now expose one domain-named module per implementation assembly.
- Data AI's internal reflection anchor now points at its semantic module; no old class-name convention remains.
- All thirteen projects build cleanly and the focused real-`AddKoan()` AI pillar bootstrap passed. The explicit
  ONNX model lane was intentionally not invoked because it is a self-executing infrastructure lane, not a
  routine focused unit gate.
- The remaining concrete legacy implementation inventory is 10 files across 10 projects.

### Application capabilities, final tranche — passed

- Data Backup, MCP Explorer, MCP Operations, Observability, Docker, Podman, and Compose rendering now expose
  one domain-named module each. All seven projects build; focused Observability (1), Data Backup (5), MCP
  Explorer (3), and MCP Operations (1) behavior passed.
- Data Backup's module contained an advertised startup inventory cache that no runtime path ever populated or
  invoked. The dead module-owned cache/validator was removed; health and diagnostics now ask the existing
  discovery service directly, keeping runtime state out of the composition owner.

### Superseded Messaging disposition — removed

- `Koan.Messaging.Core` and its InMemory/RabbitMQ connectors had no remaining production-framework consumers.
  Their only consumers were their own tests and legacy/sample projects; current Communication documentation
  already identified them as a previous-generation mechanism that did not implement Entity Transport.
- The three packages, their two obsolete bootstrap specs, and the misleading S3 legacy messaging sample were
  removed rather than migrated. Unused references were removed from current and archived samples; the current
  S8 Canon and embedded guide samples build without them, and the embedded composition lock was regenerated.
- S8 Canon remains in the solution. No production implementation of `IKoanAutoRegistrar` remains under `src`.

### Kernel and public-language deletion — passed

- Core no longer defines, discovers, generates, registers, orders, constructs, or reports
  `IKoanInitializer` or `IKoanAutoRegistrar`. `KoanModule` has no compatibility implementation and
  the registry/bootstrap/runtime contain no parallel lifecycle collections or provenance reconstruction.
- `ModuleOrdering` is the one type-safe ordering implementation and accepts only `KoanModule` owners.
  Registration failures are fail-closed `KoanBootException`s and refresh the canonical module-failure
  startup evidence before propagating.
- Current samples use domain-named modules. S7 Meridian coalesces its four startup seeders behind one
  `MeridianModule`; S8 Canon and GardenCoop rely on ordinary module activation rather than deleted registry
  inspection. Active public documentation teaches the same language.
- [ARCH-0116](../../../../decisions/ARCH-0116-one-module-lifecycle.md) records the final constitution:
  implementation assemblies own one module, cross-module contracts live in isolated contract assemblies,
  ordinary references carry activation, and `Inert`/`Required` reference metadata or equivalent crutches
  are explicitly rejected.

### Focused acceptance evidence — passed

- Core semantic activation, ordering, and retained-module behavior: 26 passed.
- Bootstrap failure, reporting, health, and manifest behavior: 12 passed.
- Communication lifecycle: 2 passed; Koan.Testing host ownership: 2 passed; Data Access module/carrier:
  15 passed.
- FirstUse contract: 1 passed, including its GoldenJourney dependency and meaningful persisted result.
- Source/staged-package generator and contract-boundary fixtures: 7 passed.
- Current GardenCoop and FirstUse builds are clean. Meridian and the solution-supported Canon Shared/API
  projects build with only their pre-existing warnings.
- One SQLite connector test project cannot currently compile because a test double has not implemented the
  newer `ResolveServiceIntent(...)` discovery member. This is unrelated to module activation and is retained
  as PMC-028 rather than widening this closure.

## Final deletion proof — passed

- source search finds no `IKoanInitializer`, `IKoanAutoRegistrar`, `RegisterInitializers`,
  `RegisterAutoRegistrars`, legacy registry getters, or legacy module bridge;
- generator output contains semantic modules plus genuinely distinct generated catalogs only;
- inactive transitive implementation modules are never constructed;
- source and staged-package FirstUse/GoldenJourney use byte-identical application bodies;
- focused startup/facts/MCP/HTTP/health assertions agree; and
- `git diff --check`, public-doc source checks, privacy checks, and the R09 meaningful-step scorecard pass.

## Stop conditions

- Stop if migration introduces a wrapper module around retained registrar objects instead of coalescing ownership.
- Stop if a contracts project gains a module or a functional dependency to avoid fixing a boundary.
- Stop if one assembly needs multiple activation owners; first determine whether it contains multiple packages.
- Stop if Core interprets pillar/provider configuration or business policy.
- Stop before release certification, publication, push, tag, release, remote mutation, or private downstream disclosure.
