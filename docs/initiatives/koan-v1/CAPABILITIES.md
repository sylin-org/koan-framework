---
type: ARCHITECTURE
domain: framework
title: "Koan V1 Capability Evidence Ledger"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-13
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
| Bootstrap, discovery, and startup reporting | assessed | `demonstrated` | [record](#bootstrap-discovery-and-startup-reporting) | Useful and fail-loud, but reporting is best-effort and the focused suite did not complete. |
| `Entity<T>` data semantics and context | assessed | `verified` | [record](#entityt-data-semantics-and-context) | Strong core semantics; external-provider parity remains test-scoped. |
| Backend discovery and negotiation | assessed | `demonstrated` | [record](#backend-discovery-and-negotiation) | The contract is explicit; fleet-wide selection and fallback are not certified. |
| Web/API conventions | assessed | `verified` | [record](#webapi-conventions) | In-memory API behavior is well tested; package installation is blocked. |
| Events and messaging | assessed | `demonstrated` | [record](#events-and-messaging) | A real sample and providers exist; no current broker conformance result was obtained. |
| Jobs and scheduling | assessed | `verified` | [record](#jobs-and-scheduling) | Core/in-process behavior is strong; distributed tiers remain separate. |
| Cache and distributed state | assessed | `verified` | [record](#cache-and-distributed-state) | Cross-engine behavior is tested; production coherence topologies are not certified. |
| AI, vector, and semantic capabilities | assessed | `experimental` | [record](#ai-vector-and-semantic-capabilities) | Strong unit/in-memory evidence, with a current host-lifecycle integration failure. |
| MCP and agent-facing surfaces | assessed | `verified` | [record](#mcp-and-agent-facing-surfaces) | Core contract passes conformance; transports and operational authorization need broader proof. |
| Authentication and authorization | assessed | `verified` | [record](#authentication-and-authorization) | Core identity passes; real external identity providers were not exercised. |
| Testing and local infrastructure | assessed | `demonstrated` | [record](#testing-and-local-infrastructure) | Useful inherited conformance exists, with skipped batteries and fragmented execution. |
| Packaging, installation, and upgrades | assessed | `specified` | [record](#packaging-installation-and-upgrades) | Intended package path is documented but public 0.17.0 packages cannot restore coherently. |
| Operations, health, and diagnostics | assessed | `demonstrated` | [record](#operations-health-and-diagnostics) | Useful primitives exist, but coverage and completeness do not justify support language. |

R02 may split or merge rows only when the resulting boundaries better match user-visible contracts.

No row is labeled `supported-*`. That is deliberate: current package installation is not coherent,
compatibility boundaries are not yet consolidated into a release contract, and several provider or
failure paths remain untested. `Verified` means the scope stated in that record is automated—not that
the entire capability fleet is production-certified.

## Capability records

Every record below was assessed by Codex on 2026-07-13 at
`4471e9c7ffeaa2cd198a62589a9763c4555d9b7f`.

### Bootstrap, discovery, and startup reporting

- **Outcome and shortest path:** call `builder.Services.AddKoan()`; referenced modules are discovered,
  activated, and summarized without application-owned registration scaffolding.
- **Entry point and owner:** [`AddKoan()`](../../../src/Koan.Core/ServiceCollectionExtensions.cs) in
  `Sylin.Koan.Core`.
- **Implementation:** [`AppBootstrapper`](../../../src/Koan.Core/Hosting/Bootstrap/AppBootstrapper.cs),
  [`KoanRegistry`](../../../src/Koan.Core/Hosting/Registry/KoanRegistry.cs), and
  [`AppRuntime`](../../../src/Koan.Core/Hosting/Runtime/AppRuntime.cs). Source-generated registrations
  are preferred; embedded manifests, assembly closure, and runtime fallbacks are intentional.
- **Executable evidence:** [`S1.Web`](../../../samples/S1.Web/Program.cs) builds. The focused
  [bootstrap suite](../../../tests/Suites/Integration/Bootstrap/Koan.Tests.Integration.Bootstrap/Koan.Tests.Integration.Bootstrap.csproj)
  did not complete within 304 seconds and produced no test result.
- **Inspection and failure:** module activation is fail-fast by default; `KOAN_BOOT_LENIENT=1` opts
  into degraded boot and a `MODULES-FAILED` report. Composition/health rendering is best-effort, and
  entity inventory is populated lazily rather than exhaustively at process start.
- **Unsupported / compatibility:** exact or exhaustive startup narration, deterministic ordering of
  every incidental background-service enumeration, and bootstrap certification across trimming/AOT
  and all deployment shapes are not established. Pre-1.0 compatibility is not promised.
- **Maturity / safe claim:** `demonstrated`. Koan discovers referenced modules and can explain major
  composition choices at startup; the report is useful but not a complete proof of runtime state.
- **Open risks:** diagnose the non-completing suite; make one structured composition model feed human,
  machine, health, and lockfile views.

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
- **Executable evidence:** data-core tests pass, including core resolution paths; capability-set tests
  cover loud unsupported behavior. [`S10.DevPortal`](../../../samples/S10.DevPortal/README.md)
  demonstrates provider switching.
- **Inspection and failure:** [`DataCompositionContributor`](../../../src/Koan.Data.Core/Composition/DataCompositionContributor.cs)
  contributes adapter elections to composition output. `CapabilityNotSupportedException` identifies
  an unsupported negotiated intent.
- **Unsupported / compatibility:** R02 did not run every relational, document, cache, vector, or
  messaging connector, nor prove fallback/election behavior for every ambiguous multi-provider graph.
- **Maturity / safe claim:** `demonstrated`. Koan has a real capability-negotiation model and reports
  major elections; only specifically tested provider combinations may claim parity.
- **Open risks:** publish a single deterministic election/explanation contract and a matrix generated
  from executable provider evidence.

### Web/API conventions

- **Outcome and shortest path:** add an [`EntityController<T>`](../../../src/Koan.Web/Controllers/EntityController.cs)
  for an entity to obtain conventional REST behavior while keeping application code domain-shaped.
- **Entry point and owner:** `Sylin.Koan.Web`, registered through
  [`ServiceCollectionExtensions`](../../../src/Koan.Web/Extensions/ServiceCollectionExtensions.cs).
- **Executable evidence:** the
  [in-memory adapter surface](../../../tests/Suites/Web/AdapterSurface/Koan.Web.AdapterSurface.InMemory.Tests/Koan.Web.AdapterSurface.InMemory.Tests.csproj)
  passes 70/70; [`S1.Web`](../../../samples/S1.Web/Program.cs) builds.
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
- **Executable evidence:** [74/74 core jobs tests](../../../tests/Suites/Jobs/Koan.Jobs.Tests/Koan.Jobs.Tests.csproj)
  pass. [`S14.AdapterBench`](../../../samples/S14.AdapterBench/Jobs/BenchmarkJob.cs) demonstrates a
  business-facing job.
- **Inspection and failure:** health snapshots/contributors, metrics, state transitions, and explicit
  ledgers/transports exist. Failures are recorded in job state rather than hidden in application glue.
- **Unsupported / compatibility:** R02 did not certify distributed competing consumers, messaging
  transport, every durable ledger, clock-skew behavior, or upgrade compatibility.
- **Maturity / safe claim:** `verified`. In-process/core job semantics are automated; distributed and
  provider-specific tiers must be claimed separately.
- **Open risks:** publish the capability ladder and expose selected ledger, transport, lane, and health
  as one machine-readable explanation.

### Cache and distributed state

- **Outcome and shortest path:** add cache intent through `Sylin.Koan.Cache` and entity-centered
  attributes/extensions; Koan resolves stores, policy, tiers, and coherence.
- **Entry point and owner:** [`CacheableAttribute`](../../../src/Koan.Cache.Abstractions/Policies/CacheableAttribute.cs),
  [`EntityCacheExtensions`](../../../src/Koan.Cache/Extensions/EntityCacheExtensions.cs), and
  [`CacheAdapterResolver`](../../../src/Koan.Cache/Adapters/CacheAdapterResolver.cs).
- **Executable evidence:** the
  [cross-engine suite](../../../tests/Suites/Cache/CrossEngine/Koan.Tests.Cache.CrossEngine/Koan.Tests.Cache.CrossEngine.csproj)
  passes 14/14; additional topology, coherence, Redis, and SQLite projects exist but were not all run.
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
  pass 152/152 and the
  [in-memory vector surface](../../../tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.InMemory.Tests/Koan.Data.VectorAdapterSurface.InMemory.Tests.csproj)
  passes 33/33. [Data/AI integration](../../../tests/Suites/Data/AI/Koan.Data.AI.Tests/Koan.Data.AI.Tests.csproj)
  fails 1 of 79 on a disposed host service provider.
- **Inspection and failure:** models and vector capabilities are registrable/queryable, but lifecycle
  failure currently leaks a low-level `ObjectDisposedException` rather than a corrective Koan error.
- **Unsupported / compatibility:** host teardown/restart safety, external model servers, every vector
  database, background embedding durability, model migration, cost/rate behavior, and semantic parity
  are not certified.
- **Maturity / safe claim:** `experimental`. Koan has useful verified AI building blocks and in-memory
  vector behavior, but the combined lifecycle is not foundation-ready.
- **Open risks:** fix and regress the lifecycle failure; define model/provider negotiation and observable
  embedding state before positioning AI as a stable property of every entity.

### MCP and agent-facing surfaces

- **Outcome and shortest path:** annotate an entity with
  [`McpEntityAttribute`](../../../src/Koan.Mcp/McpEntityAttribute.cs) to project governed entity
  operations and resources through MCP.
- **Entry point and owner:** `Sylin.Koan.Mcp`; registration and transports live under
  [`Koan.Mcp`](../../../src/Koan.Mcp/Koan.Mcp.csproj), with optional operations and explorer packages.
- **Executable evidence:** [MCP conformance](../../../tests/Suites/Mcp/Koan.Mcp.Conformance.Tests/Koan.Mcp.Conformance.Tests.csproj)
  passes 72/72. Custom-tool, streamable transport, relationship visibility, operations, and explorer
  suites exist but were not included in this focused result.
- **Inspection and failure:** entity catalog/self resources, schemas, tool hints, dry-run, provenance,
  correlation, access policy, and response translation give agents a structured surface rather than
  source scraping alone.
- **Unsupported / compatibility:** not every transport, explorer path, production edge-auth policy,
  sandbox, code-mode behavior, or hostile-client scenario is certified by R02.
- **Maturity / safe claim:** `verified`. The core MCP projection and invocation contract passes its
  conformance suite; deployment-specific safety claims require their own evidence.
- **Open risks:** align agent-visible errors and composition facts with the human startup report, and
  make safe operational boundaries unmistakable by default.

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
  report 10 passed and 3 skipped; cache and embedding batteries were skipped. The repository contains
  many focused suites, but the bootstrap suite did not complete under a routine invocation.
- **Inspection and failure:** capability-gated conformance can explain why some batteries do not apply,
  but current skip/timeout behavior is not yet a single reliable developer feedback loop.
- **Unsupported / compatibility:** inherited testing does not presently guarantee every entity/module
  combination, external infrastructure lifecycle, deterministic full-suite execution, or stable public
  test-kit APIs.
- **Maturity / safe claim:** `demonstrated`. Koan provides useful reusable conformance tests; applications
  do not yet inherit a fully verified, package-installable test contract.
- **Open risks:** make skipped capability reasons explicit, establish bounded test lanes, and ship a
  coherent package-first application test.

### Packaging, installation, and upgrades

- **Outcome and intended path:** create a new web project, add coherent `Sylin.Koan.*` packages, call
  `AddKoan()`, and reach the first entity/API without cloning framework source.
- **Entry point and owner:** repository-wide package metadata in
  [`Directory.Build.props`](../../../Directory.Build.props), compatibility ranges in
  [`build/compat-ranges.targets`](../../../build/compat-ranges.targets), and NBGV
  [`version.json`](../../../version.json).
- **Executable evidence:** source `S1.Web` builds. A disposable exact-0.17.0 NuGet application fails
  restore because `Sylin.Koan.Data.Abstractions` requires Core `>= 0.17.3`, while public Core is
  0.17.0. The probe also reports a high-severity advisory in a transitive SQLite native dependency.
- **Inspection and failure:** the restore fails loudly, which is safer than an ABI mismatch, but the
  public docs previously described packages only as lagging and offered a broken copy/paste path.
- **Unsupported / compatibility:** package-first install, package-set coherence, upgrades, rollback,
  migration, release cadence, and support windows are not established at this snapshot.
- **Maturity / safe claim:** `specified`. The package model and compatibility-range intent are written;
  the current public package set is not a supported installation path. Source checkout is the only
  demonstrated path.
- **Open risks:** R04 priority zero is an atomic, advisory-clean package set plus an external clean-room
  restore/run test and an explicit pre-1.0 upgrade policy.

### Operations, health, and diagnostics

- **Outcome and shortest path:** inspect boot output, health aggregation, capability sets, and
  composition contributors to understand what the application selected and whether it is ready.
- **Entry point and owner:** core health under
  [`Koan.Core/Observability`](../../../src/Koan.Core/Observability), plus
  [`Sylin.Koan.Observability`](../../../src/Koan.Observability/Koan.Observability.csproj) and module
  contributors.
- **Executable evidence:** the
  [observability suite](../../../tests/Suites/Observability/Koan.Observability.Tests/Koan.Observability.Tests.csproj)
  passes 1/1. Startup/health implementations are exercised indirectly elsewhere, but the bootstrap
  suite did not complete.
- **Inspection and failure:** health aggregation and provider contributors are real; startup rendering
  intentionally catches reporting failures so diagnostics do not necessarily prevent application boot.
- **Unsupported / compatibility:** exhaustive composition, stable machine-readable schemas, production
  telemetry pipelines, redaction proof, alerting/SLO contracts, and every provider health contributor
  are not certified.
- **Maturity / safe claim:** `demonstrated`. Koan offers useful startup, capability, and health
  inspection primitives; operators should not treat the current report as a complete source of truth.
- **Open risks:** define one structured explanation model, test redaction and degraded states, and make
  human, agent, reviewer, and telemetry projections consistent.

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
