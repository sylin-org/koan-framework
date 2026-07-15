---
type: ARCHITECTURE
domain: framework
title: "Koan V1 Capability Evidence Ledger"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-14
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-14
  status: reviewed
  scope: evidence vocabulary and initial assessment queue
---

# Koan V1 Capability Evidence Ledger

This ledger controls what the initiative may claim about Koan. A capability is not supported merely
because an API, sample, or document exists. Record the implementation, executable evidence,
documented contract, known limits, and ownership before promoting a claim.

[`PROGRESS.md`](PROGRESS.md) tracks work. This file tracks product truth. R02 assessed the current
snapshot conservatively; the command-level record is in [`R02-EVIDENCE.md`](R02-EVIDENCE.md).

## Maturity vocabulary

Use one of these labels for every assessed capability:

| Label | Meaning |
|---|---|
| `specified` | The intended contract is written, but executable evidence is absent or incomplete. |
| `demonstrated` | A maintained sample or focused exercise proves a useful path; compatibility is not yet promised. |
| `experimental` | Demonstrated or selectively verified, but intentionally outside compatibility guarantees. |
| `verified` | Automated tests cover the stated contract and its important failure modes. |
| `supported-extension` | Verified, documented, packaged, and within an explicit compatibility boundary. |
| `supported-foundation` | A stable, verified default that other supported Koan capabilities may rely on. |
| `deprecated` | Still present for migration, with a named replacement and removal policy. |
| `retired` | No longer part of the supported product surface. |

`Unassessed` is an assessment state, not a maturity label. Do not translate it into a public claim.

## Evidence requirements

An assessed entry must identify:

1. the user outcome and shortest supported path;
2. the public entry point and owning package;
3. current code locations;
4. automated tests and what they actually prove;
5. maintained samples or executable documentation;
6. startup, error, and inspection behavior;
7. supported and unsupported scenarios;
8. the compatibility or removal expectation;
9. the date and commit assessed.

Private downstream use may reveal questions, but it is neither citable evidence nor a public claim.
Convert a lesson into an anonymous repository-owned test, sample, issue, or decision before relying on
it here.

## Assessed capability summary

| Surface | Assessment state | Maturity | Evidence record | Principal question |
|---|---|---|---|---|
| Bootstrap, discovery, and startup reporting | assessed | `demonstrated` | [record](#bootstrap-discovery-and-startup-reporting) | Bounded lanes pass; a shared fact model now proves one vertical slice, not exhaustive runtime narration. |
| `Entity<T>` data semantics and context | assessed | `verified` | [record](#entityt-data-semantics-and-context) | Strong core semantics; external-provider parity remains test-scoped. |
| Backend discovery and negotiation | assessed | `demonstrated` | [record](#backend-discovery-and-negotiation) | Child-edge cost is explicit and bounded for three proven providers; fleet-wide parity is not certified. |
| Web/API conventions | assessed | `verified` | [record](#webapi-conventions) | In-memory API behavior is well tested; package installation is blocked. |
| Events and messaging | assessed | `demonstrated` | [record](#events-and-messaging) | A real sample and providers exist; no current broker conformance result was obtained. |
| Jobs and scheduling | assessed | `verified` | [record](#jobs-and-scheduling) | Core/in-process behavior is strong; distributed tiers remain separate. |
| Cache and distributed state | assessed | `verified` | [record](#cache-and-distributed-state) | Cross-engine behavior is tested; production coherence topologies are not certified. |
| AI, vector, and semantic capabilities | assessed | `experimental` | [record](#ai-vector-and-semantic-capabilities) | Strong unit/in-memory evidence, with a current host-lifecycle integration failure. |
| MCP and agent-facing surfaces | assessed | `verified` | [record](#mcp-and-agent-facing-surfaces) | Core contract passes conformance; transports and operational authorization need broader proof. |
| Authentication and authorization | assessed | `verified` | [record](#authentication-and-authorization) | Core identity passes; real external identity providers were not exercised. |
| Testing and local infrastructure | assessed | `demonstrated` | [record](#testing-and-local-infrastructure) | Bounded bootstrap and FirstUse lanes are coherent; inherited batteries still contain explicit skips. |
| Packaging, installation, and upgrades | assessed | `specified` | [record](#packaging-installation-and-upgrades) | Local release artifacts pass the real FirstUse clean room; public 0.17.0 remains incoherent. |
| Operations, health, and diagnostics | assessed | `demonstrated` | [record](#operations-health-and-diagnostics) | Schema-1 runtime facts are verified for module/default-data decisions; fleet completeness remains open. |

R02 may split or merge rows only when the resulting boundaries better match user-visible contracts.

No row is labeled `supported-*`. That is deliberate: current package installation is not coherent,
compatibility boundaries are not yet consolidated into a release contract, and several provider or
failure paths remain untested. `Verified` means the scope stated in that record is automated—not that
the entire capability fleet is production-certified.

## Capability records

Every record below was initially assessed by Codex on 2026-07-13 at
`4471e9c7ffeaa2cd198a62589a9763c4555d9b7f`. R04 evidence amendments are dated in their text and the
initiative progress ledger; they do not silently promote the original maturity labels.

### Bootstrap, discovery, and startup reporting

- **Outcome and shortest path:** call `builder.Services.AddKoan()`; referenced modules are discovered,
  activated, and summarized without application-owned registration scaffolding.
- **Entry point and owner:** [`AddKoan()`](../../../src/Koan.Core/ServiceCollectionExtensions.cs) in
  `Sylin.Koan.Core`.
- **Implementation:** [`AppBootstrapper`](../../../src/Koan.Core/Hosting/Bootstrap/AppBootstrapper.cs),
  [`KoanRegistry`](../../../src/Koan.Core/Hosting/Registry/KoanRegistry.cs), and
  [`AppRuntime`](../../../src/Koan.Core/Hosting/Runtime/AppRuntime.cs). Source-generated registrations
  are preferred; embedded manifests, assembly closure, and runtime fallbacks are intentional.
- **Executable evidence:** [`FirstUse`](../../../samples/FirstUse/Program.cs) builds and its source
  contract passes through the 15/15 packaging suite. The bounded bootstrap lanes pass Fast 17/17,
  offline Pillars 16/16, and explicit Infrastructure 7/7. Core passes 211/211.
- **Inspection and failure:** module activation is fail-fast by default; `KOAN_BOOT_LENIENT=1` opts
  into degraded boot. Activation/rejection and default data election now enter the shared
  [runtime fact envelope](../../engineering/runtime-facts.md), which projects corrections into
  startup, exception, health, lockfile, Web, and MCP views. Entity inventory remains lazy rather than
  exhaustive at process start.
- **Unsupported / compatibility:** exact or exhaustive startup narration, deterministic ordering of
  every incidental background-service enumeration, and bootstrap certification across trimming/AOT
  and all deployment shapes are not established. Pre-1.0 compatibility is not promised.
- **Maturity / safe claim:** `demonstrated`. Koan discovers referenced modules and can explain major
  composition choices at startup; the report is useful but not a complete proof of runtime state.
- **Open risks:** extend the proven fact path to other negotiations without claiming exhaustive runtime
  state or admitting arbitrary provider payloads.

### `Entity<T>` data semantics and context

- **Outcome and shortest path:** inherit [`Entity<T>`](../../../src/Koan.Data.Core/Model/Entity.cs) and
  use `Get`, `Query`, `Save`, `Remove`, and streaming operations from business code without an
  application repository or `DbContext` layer.
- **Entry point and owner:** `Koan.Data.Core.Model.Entity<T>` in `Sylin.Koan.Data.Core`; ambient behavior
  is carried by [`EntityContext`](../../../src/Koan.Data.Core/EntityContext.cs) and
  [`EntityEventContext`](../../../src/Koan.Data.Core/Events/EntityEventContext.cs).
- **Executable evidence:** [283 data-core tests](../../../tests/Suites/Data/Core/Koan.Tests.Data.Core/Koan.Tests.Data.Core.csproj)
  pass. [`S0.ConsoleJsonRepo`](../../../samples/S0.ConsoleJsonRepo/Program.cs) and
  [`S1.Web`](../../../samples/S1.Web/Todo.cs) demonstrate console and web paths.
- **Inspection and failure:** repositories and capabilities are resolvable through the static data
  facade; missing/unsupported operations use explicit exceptions rather than silent client-side
  emulation where the capability contract applies.
- **Unsupported / compatibility:** the result does not certify semantic parity for every external
  database, transaction shape, query expression, tenancy mode, or migration path. The surface is
  pre-1.0.
- **Maturity / safe claim:** `verified`. Core `Entity<T>` persistence semantics are automated and are
  Koan's strongest first-class application language; provider-specific claims require their suites.
- **Open risks:** R03 must define which extensions belong on Entity, which context flows are stable,
  and how capability additions remain IntelliSense-coherent.

### Backend discovery and negotiation

- **Outcome and shortest path:** reference a connector, optionally select it by configuration or
  attribute, and inspect its declared capabilities before invoking provider-specific behavior.
- **Entry point and owner:** [`AdapterResolver`](../../../src/Koan.Data.Core/AdapterResolver.cs),
  [`DataAdapterAttribute`](../../../src/Koan.Data.Abstractions/DataAdapterAttribute.cs),
  [`DataCaps`](../../../src/Koan.Data.Abstractions/Capabilities/DataCaps.cs), and core
  [`CapabilitySet`](../../../src/Koan.Core/Capabilities/CapabilitySet.cs).
- **Executable evidence:** Data.Core passes 299/299. Relationship cells execute InMemory, JSON, and
  SQLite selection/rejection/bounds; Web relationship passes 7/7; MCP relationship passes 2/2 and MCP
  conformance 73/73. [`S10.DevPortal`](../../../samples/S10.DevPortal/README.md) demonstrates provider switching.
- **Inspection and failure:** [`AdapterResolutionDecision`](../../../src/Koan.Data.Core/Routing/AdapterResolutionDecision.cs)
  is the single calculation for configured/default data selection used by runtime behavior, lockfile,
  and schema-1 facts. [ARCH-0112](../../decisions/ARCH-0112-bounded-relationship-negotiation.md) adds
  physical filter profiles, corrective relationship rejections, and the latest safe execution fact.
- **Unsupported / compatibility:** R02 did not run every relational, document, cache, vector, or
  messaging connector, nor prove fallback/election behavior for every ambiguous multi-provider graph.
- **Maturity / safe claim:** `demonstrated`. Koan has a real capability-negotiation model and reports
  major elections; only specifically tested provider combinations may claim parity.
- **Open risks:** execute the remaining provider cells; verify index sufficiency and performance;
  design parent batching and recursive graph/depth budgets without turning facts into request history.

### Web/API conventions

- **Outcome and shortest path:** add an [`EntityController<T>`](../../../src/Koan.Web/Controllers/EntityController.cs)
  for an entity to obtain conventional REST behavior while keeping application code domain-shaped.
- **Entry point and owner:** `Sylin.Koan.Web`, registered through
  [`ServiceCollectionExtensions`](../../../src/Koan.Web/Extensions/ServiceCollectionExtensions.cs).
- **Executable evidence:** the
  [in-memory adapter surface](../../../tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.InMemory.Tests/Koan.Web.AdapterSurface.InMemory.Tests.csproj)
  passes 70/70; [`FirstUse`](../../../samples/FirstUse/README.md) creates and reads a real approval
  over SQLite in both source and package-only lanes.
- **Inspection and failure:** web operations reuse the same endpoint/entity service semantics as MCP;
  health and well-known surfaces are separately implemented. Provider capability failures remain
  explicit.
- **Unsupported / compatibility:** external adapter web suites, all OpenAPI/authorization combinations,
  production hosting topologies, and package-first creation were not certified in R02.
- **Maturity / safe claim:** `verified`. The tested in-memory entity-controller contract produces a
  meaningful API with little application ceremony; provider and package claims need separate proof.
- **Open risks:** convert the shortest path into an executable golden journey with asserted HTTP and
  startup output.

### Events and messaging

- **Outcome and shortest path:** reference a messaging provider and call entity/message-centered
  extension methods such as `Send()`; provider registration is automatic.
- **Entry point and owner:** `Koan.Messaging` plus the
  [in-memory](../../../src/Connectors/Messaging/InMemory/Koan.Messaging.Connector.InMemory.csproj) or
  [RabbitMQ](../../../src/Connectors/Messaging/RabbitMq/Koan.Messaging.Connector.RabbitMq.csproj)
  connector.
- **Executable evidence:** [`S3.Mq.Sample`](../../../samples/S3.Mq.Sample/README.md) is a maintained
  end-to-end RabbitMQ sample. Messaging-dependent cache/jobs tests exist, but no broker-backed core
  conformance result was obtained in R02; the umbrella bootstrap suite did not complete.
- **Inspection and failure:** provider discovery exists, but there is no R02-certified unified account
  of topology, delivery guarantees, retries, and rejected capabilities in startup output.
- **Unsupported / compatibility:** do not claim broker parity, exactly-once delivery, durable outbox,
  or production topology support from current evidence. The catalog's planned S3/S9 rows are not
  evidence for shipped behavior.
- **Maturity / safe claim:** `demonstrated`. Koan can send through real in-memory/RabbitMQ paths; delivery
  and compatibility guarantees are not yet consolidated.
- **Open risks:** establish a provider-neutral messaging conformance suite and make delivery semantics
  inspectable before promotion.

### Jobs and scheduling

- **Outcome and shortest path:** implement `IKoanJob<T>` on a job entity and use the same entity/data
  language for execution, state, progress, retries, and scheduling.
- **Entry point and owner:** [`IKoanJob`](../../../src/Koan.Jobs/IKoanJob.cs) and
  [`JobsServiceCollectionExtensions`](../../../src/Koan.Jobs/JobsServiceCollectionExtensions.cs) in
  `Sylin.Koan.Jobs`.
- **Executable evidence:** [76/76 core jobs tests](../../../tests/Suites/Jobs/Koan.Jobs.Tests/Koan.Jobs.Tests.csproj)
  and 78/78 SQLite ledger tests pass. [`S14.AdapterBench`](../../../samples/S14.AdapterBench/Jobs/BenchmarkJob.cs) demonstrates a
  business-facing job. The shared terminal-progress contract additionally passes against the
  in-memory and SQLite ledgers, and [`GoldenJourney`](../../../samples/GoldenJourney/README.md)
  observes the completed business priority and 100% progress from a running source application.
- **Inspection and failure:** health snapshots/contributors, metrics, state transitions, and explicit
  ledgers/transports exist. Failures are recorded in job state rather than hidden in application glue.
- **Unsupported / compatibility:** R02 did not certify distributed competing consumers, messaging
  transport, every durable ledger, clock-skew behavior, or upgrade compatibility.
- **Maturity / safe claim:** `verified`. In-process/core job semantics are automated; distributed and
  provider-specific tiers must be claimed separately.
- **Open risks:** distributed transport/ledger tiers still need their own composition and behavior
  evidence; lane and scheduling elections are not yet part of the common fact envelope.

### Cache and distributed state

- **Outcome and shortest path:** add cache intent through `Sylin.Koan.Cache` and entity-centered
  attributes/extensions; Koan resolves stores, policy, tiers, and coherence.
- **Entry point and owner:** [`CacheableAttribute`](../../../src/Koan.Cache.Abstractions/Policies/CacheableAttribute.cs),
  the module-owned [`EntityCacheFacet`](../../../src/Koan.Cache/Entity/EntityCacheFacet.cs),
  [`EntityCacheExtensions`](../../../src/Koan.Cache/Extensions/EntityCacheExtensions.cs), and
  [`CacheAdapterResolver`](../../../src/Koan.Cache/Adapters/CacheAdapterResolver.cs).
- **Executable evidence:** the
  [cross-engine suite](../../../tests/Suites/Cache/CrossEngine/Koan.Tests.Cache.CrossEngine/Koan.Tests.Cache.CrossEngine.csproj)
  passes 14/14; the
  [Entity-language consumer suite](../../../tests/Suites/EntityLanguage/Koan.Tests.EntityLanguage/Koan.Tests.EntityLanguage.csproj)
  passes 9/9 for module absence/presence/removal, receiver validity, collision safety, explanation,
  compatibility operations, corrective failure, and repeated-host resolution. Additional topology,
  coherence, Redis, and SQLite projects exist but were not all run.
- **Inspection and failure:** topology and capabilities are explicit; instrumentation, trace filtering,
  and health checks exist. An obsolete compatibility shim still produces build warnings.
- **Unsupported / compatibility:** R02 does not certify cross-node failure recovery, Redis production
  behavior, every invalidation race, or all consistency modes.
- **Maturity / safe claim:** `verified`. The tested cache contract and cross-engine semantics pass;
  production coherence guarantees remain topology- and provider-specific.
- **Open risks:** eliminate compatibility residue and make freshness, fallback, and invalidation facts
  visible in startup/health output.

### AI, vector, and semantic capabilities

- **Outcome and shortest path:** annotate or extend an entity for embeddings and semantic search while
  choosing AI/vector connectors by reference and configuration.
- **Entry point and owner:** `Sylin.Koan.Data.AI`, `Sylin.Koan.Data.Vector`, and connector packages;
  vector capability vocabulary starts at [`VectorCaps`](../../../src/Koan.Data.Vector.Abstractions/Capabilities/VectorCaps.cs).
- **Executable evidence:** [AI unit tests](../../../tests/Suites/AI/Unit/Koan.Tests.AI.Unit/Koan.Tests.AI.Unit.csproj)
  pass 157/157 and the
  [in-memory vector surface](../../../tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.InMemory.Tests/Koan.Data.VectorAdapterSurface.InMemory.Tests.csproj)
  passes 33/33. [Data/AI integration](../../../tests/Suites/Data/AI/Koan.Data.AI.Tests/Koan.Data.AI.Tests.csproj)
  passes 82/82 after the R04-02 repeated-host repairs.
- **Inspection and failure:** models and vector capabilities are registrable/queryable. Common required
  AI client paths expose a typed host-context failure; optional availability probes remain nullable.
- **Unsupported / compatibility:** external model servers, every vector database, background
  embedding durability, model migration, cost/rate behavior, and semantic parity are not certified.
- **Maturity / safe claim:** `experimental`. Koan has useful verified AI building blocks and in-memory
  vector behavior, but the combined lifecycle is not foundation-ready.
- **Open risks:** define model/provider negotiation and observable embedding state before positioning AI
  as a stable property of every entity.

### MCP and agent-facing surfaces

- **Outcome and shortest path:** annotate an entity with
  [`McpEntityAttribute`](../../../src/Koan.Mcp/McpEntityAttribute.cs) to project governed entity
  operations and resources through MCP.
- **Entry point and owner:** `Sylin.Koan.Mcp`; registration and transports live under
  [`Koan.Mcp`](../../../src/Koan.Mcp/Koan.Mcp.csproj), with optional operations and explorer packages.
- **Executable evidence:** [MCP conformance](../../../tests/Suites/Mcp/Koan.Mcp.Conformance.Tests/Koan.Mcp.Conformance.Tests.csproj)
  passes 73/73, including custom-tool projection and caller-specific `koan://self` behavior alongside
  canonical `koan://facts` serialization. `FirstUseContractTests` additionally prove Streamable HTTP
  initialization, resource discovery, identical Web/MCP facts, remote-origin operation filtering,
  dry-run non-mutation, and a real agent upsert observed through REST. The cumulative GoldenJourney
  source contract proves that a custom-tool-only application describes both live workflows through
  `koan://self` before an agent mutation is observed through REST.
- **Inspection and failure:** entity catalog/self resources, schemas, tool hints, dry-run, provenance,
  correlation, access policy, response translation, and the host fact envelope give agents a
  structured surface rather than source or log scraping.
- **Unsupported / compatibility:** not every transport, explorer path, production edge-auth policy,
  sandbox, code-mode behavior, or hostile-client scenario is certified by R02.
- **Maturity / safe claim:** `verified`. The core MCP projection and invocation contract passes its
  conformance suite; deployment-specific safety claims require their own evidence.
- **Open risks:** expand fact coverage beyond the current module/default-data slice and make safe
  operational boundaries unmistakable by default.

### Authentication and authorization

- **Outcome and shortest path:** reference the relevant identity/auth module and express access through
  policies, roles, credentials, or identity extensions rather than application-owned protocol glue.
- **Entry point and owner:** [`Koan.Identity`](../../../src/Koan.Identity/Koan.Identity.csproj),
  [`Koan.Identity.Web`](../../../src/Koan.Identity.Web/Koan.Identity.Web.csproj), and web auth packages.
- **Executable evidence:** [identity integration](../../../tests/Suites/Integration/Identity/Koan.Identity.Tests/Koan.Identity.Tests.csproj)
  passes 113/113. Current OIDC integration tests exercise the maintained ASP.NET handler path rather
  than the obsolete placeholder callback.
- **Inspection and failure:** audit, effective-access explanation, sessions, impersonation, tenancy,
  MFA, and credential primitives exist. Security posture depends on which optional modules and host
  policies are selected.
- **Unsupported / compatibility:** no claim is made for every external provider, production key
  management, federation topology, regulatory posture, or secure deployment without operator-owned
  configuration.
- **Maturity / safe claim:** `verified`. Core identity behavior is automated; Koan does not make a
  sample or default configuration a blanket security certification.
- **Open risks:** document secure responsibility boundaries and create external-provider smoke lanes
  before `supported-extension` language.

### Testing and local infrastructure

- **Outcome and shortest path:** derive one entity-specific class from
  [`EntityConformanceSpecs<T>`](../../../src/Koan.Testing/EntityConformanceSpecs.cs) and host it through
  [`KoanIntegrationHost`](../../../src/Koan.Testing.Hosting/KoanIntegrationHost.cs).
- **Entry point and owner:** `Sylin.Koan.Testing` and `Sylin.Koan.Testing.Hosting`.
- **Executable evidence:** [testing meta-tests](../../../tests/Suites/Testing/Koan.Testing.Tests/Koan.Testing.Tests.csproj)
  report 11 passed and 3 intentional skips; cache and embedding batteries were skipped. Bootstrap is
  split into bounded 17/17 Fast, 16/16 Pillars, and 7/7 Infrastructure lanes. The 15/15 packaging suite
  includes serialized isolated source-checkout FirstUse and GoldenJourney process proofs.
- **Inspection and failure:** capability-gated conformance can explain why some batteries do not apply;
  bounded runners report the lane, phase, command, deadline, and captured diagnostics on failure.
- **Unsupported / compatibility:** inherited testing does not presently guarantee every entity/module
  combination, external infrastructure lifecycle, deterministic full-suite execution, or stable public
  test-kit APIs.
- **Maturity / safe claim:** `demonstrated`. Koan provides useful reusable conformance tests; applications
  do not yet inherit a fully verified, package-installable test contract.
- **Open risks:** make every skipped capability reason explicit and consolidate the remaining
  provider-specific runners without accidentally starting infrastructure from routine solution tests.

### Packaging, installation, and upgrades

- **Outcome and intended path:** create a new web project, add coherent `Sylin.Koan.*` packages, call
  `AddKoan()`, and reach the first entity/API without cloning framework source.
- **Entry point and owner:** repository-wide package metadata in
  [`Directory.Build.props`](../../../Directory.Build.props), compatibility ranges in
  [`build/compat-ranges.targets`](../../../build/compat-ranges.targets), and NBGV
  [`version.json`](../../../version.json).
- **Executable evidence:** the release compiler inventories 113 independently versioned owners. A
  fresh Git-derived rehearsal selected and verified 84 packages: 45 changed versions and 39
  unpublished-current registry repairs. Its package-only clean room copied the exact public
  [`FirstUse`](../../../samples/FirstUse/FirstUse.csproj) and
  [`GoldenJourney`](../../../samples/GoldenJourney/GoldenJourney.csproj) apps outside the checkout,
  hydrated one local feed, and passed FirstUse 8/8 in 4.129s plus GoldenJourney 11/11 in 8.769s. Both
  external restores/builds emitted zero warnings and zero errors. Separately, a
  disposable exact-0.17.0 public-NuGet application fails restore because
  `Sylin.Koan.Data.Abstractions` requires Core `>= 0.17.3`, while public Core is 0.17.0; that probe also
  reports a high-severity advisory in a transitive SQLite native dependency.
- **Current R05 change:** the fresh package proof embeds one source commit across independently
  selected App, SQLite, Jobs, and MCP versions and writes separate evidence for both applications.
  This proves local artifact coherence; it is not a public package-path claim because the release set
  was not published.
- **Inspection and failure:** the restore fails loudly, which is safer than an ABI mismatch, but the
  public docs previously described packages only as lagging and offered a broken copy/paste path.
- **Unsupported / compatibility:** the staged package set is coherent, but public package-first install,
  upgrades, rollback, migration, release cadence, and support windows are not established until the
  automated `dev` release is actually published and observed.
- **Maturity / safe claim:** `specified`. The package model and compatibility-range intent are written;
  the current public package set is not a supported installation path. Source checkout is the only
  demonstrated path.
- **Open risks:** observe the first trusted `dev` publication, retain advisory review, and add explicit
  pre-1.0 upgrade/rollback policy before promoting the package path.

### Operations, health, and diagnostics

- **Outcome and shortest path:** inject `IKoanRuntimeFacts`, inspect boot output/health, request the
  gated `/.well-known/Koan/facts` endpoint, or read `koan://facts` to understand the decisions in the
  migrated runtime slice.
- **Entry point and owner:** [`IKoanRuntimeFacts`](../../../src/Koan.Core/Diagnostics/IKoanRuntimeFacts.cs)
  and core health under [`Koan.Core/Observability`](../../../src/Koan.Core/Observability), plus
  [`Sylin.Koan.Observability`](../../../src/Koan.Observability/Koan.Observability.csproj) and module
  contributors.
- **Executable evidence:** focused facts pass 7/7; Core 211/211, Data.Core 299/299, Web WellKnown 3/3,
  Web relationship 7/7, MCP relationship 2/2, and MCP conformance 73/73. These prove ordering, schema round-trip, redaction, host isolation,
  unknown/degraded health, and identical Web/MCP serialization for the vertical slice. FirstUse proves
  the operator and agent projections against the same running SQLite-backed business application.
- **Inspection and failure:** collection starts incomplete/unknown; safe collection failures and
  rejected/degraded facts cannot silently become healthy. Human output is a projection and machine
  views use canonical schema-1 JSON.
- **Unsupported / compatibility:** exhaustive composition, all provider negotiations, production
  telemetry pipelines, alerting/SLO contracts, and every provider health contributor are not
  certified. Schema changes require a new version; exact human formatting is not a contract.
- **Maturity / safe claim:** `demonstrated`. Koan offers one verified fact model and consistent
  projections for module activation and default data election; operators must not treat current
  coverage as exhaustive runtime truth.
- **Open risks:** migrate other provider negotiations through the model, and add
  provider-owned detail surfaces without weakening redaction or schema stability.

## Entry template

```markdown
### <Capability>

- Assessment date and commit:
- Assessor:
- User outcome:
- Shortest supported path:
- Public entry point / package:
- Implementation:
- Automated evidence:
- Maintained example:
- Startup and inspection behavior:
- Supported scenarios:
- Unsupported scenarios:
- Compatibility expectation:
- Maturity:
- Claim safe to publish:
- Open risks:
```

## Promotion rule

A claim may move upward only when all evidence required by its target label is linked and the relevant
work item passes [`ACCEPTANCE.md`](ACCEPTANCE.md). Absence of a known failure is not evidence. A single
private deployment is not evidence. A sample without assertions is demonstration, not verification.
