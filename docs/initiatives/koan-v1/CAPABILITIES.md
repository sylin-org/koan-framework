---
type: ARCHITECTURE
domain: framework
title: "Koan V1 Capability Evidence Ledger"
audience: [architects, maintainers, ai-agents]
status: draft
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
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
| `Entity<T>` data semantics and context | assessed | `verified` | [record](#entityt-data-semantics-and-context) | Entity streams are provider-bounded on six qualified adapters and fail closed on complete-scan resident adapters. |
| Backend discovery and negotiation | assessed | `demonstrated` | [record](#backend-discovery-and-negotiation) | Streaming selection/rejection is capability-driven and runtime-explained; fleet-wide behavioral parity is not certified. |
| Web/API conventions | assessed | `verified` | [record](#webapi-conventions) | In-memory API behavior is well tested; package installation is blocked. |
| Entity Events and Transport | assessed | `verified` | [record](#entity-events-and-transport) | The zero-config local ring and named RabbitMQ Transport channels have focused conformance; heterogeneous contracts, retries, and remote settlement remain unsupported. |
| Jobs and scheduling | assessed | `verified` | [record](#jobs-and-scheduling) | Core/in-process behavior is strong; distributed tiers remain separate. |
| Cache and distributed state | assessed | `verified` | [record](#cache-and-distributed-state) | Cross-engine behavior is tested; production coherence topologies are not certified. |
| Media processing and serving | assessed | `verified` | [record](#media-processing-and-serving) | Recipe and HTTP behavior are strong; prewarm, automatic cleanup, and multi-source routing are unsupported. |
| AI, vector, and semantic capabilities | assessed | `experimental` | [record](#ai-vector-and-semantic-capabilities) | Strong unit/in-memory evidence; external providers and the combined production lifecycle remain uncertified. |
| MCP and agent-facing surfaces | assessed | `verified` | [record](#mcp-and-agent-facing-surfaces) | Core contract passes conformance; transports and operational authorization need broader proof. |
| Authentication and authorization | assessed | `verified` | [record](#authentication-and-authorization) | Core identity passes; real external identity providers were not exercised. |
| Testing and local infrastructure | assessed | `demonstrated` | [record](#testing-and-local-infrastructure) | Bounded lanes and concurrent conformance host isolation are coherent; the ring support boundary remains open. |
| Packaging, installation, and upgrades | assessed | `specified` | [record](#packaging-installation-and-upgrades) | Local release artifacts pass the real FirstUse clean room; public 0.17.0 remains incoherent. |
| Operations, health, and diagnostics | assessed | `demonstrated` | [record](#operations-health-and-diagnostics) | Schema-1 runtime facts are verified for module/default-data decisions; fleet completeness remains open. |

R02 may split or merge rows only when the resulting boundaries better match user-visible contracts.

No row is labeled `supported-*`. That is deliberate: current public package installation is not
coherent, the locally proved compatibility/release contract has not yet been observed through a real
publication, and several provider or failure paths remain untested. `Verified` means the scope stated
in that record is automated—not that the entire capability fleet is production-certified.

## Capability records

Every record below was initially assessed by Codex on 2026-07-13 at
`4471e9c7ffeaa2cd198a62589a9763c4555d9b7f`. R04 evidence amendments are dated in their text and the
initiative progress ledger; they do not silently promote the original maturity labels.

R05 closure adds one cumulative source/package journey, independent-reader evidence, backend
rejection/recovery, and converged operator/agent inspection. It strengthens the records below but does
not promote any maturity label: the coherent package closure has not been published and compatibility
boundaries remain pre-V1 work.

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
- **Entry point and owner:** `Koan.Data.Core.Model.Entity<T>` in `Sylin.Koan.Data.Core`. Data routing,
  cache, and transaction intent remain in [`EntityContext`](../../../src/Koan.Data.Core/EntityContext.cs);
  generic logical-flow state and durable carriage now live in
  [`Koan.Core.Context`](../../../src/Koan.Core/Context/) through `KoanContext`,
  `IKoanContextCarrier`, and the host-owned `KoanContextCarrierRegistry`.
- **Executable evidence:** Data.Core passes 349/349, including current health-participation and
  observed-Entity diagnostics. [`S0.ConsoleJsonRepo`](../../../samples/S0.ConsoleJsonRepo/Program.cs) and
  [`S1.Web`](../../../samples/S1.Web/Todo.cs) demonstrate console and web paths.
- **Inspection and failure:** repositories and capabilities are resolvable through the static data
  facade; missing/unsupported operations use explicit exceptions rather than silent client-side
  emulation where the capability contract applies.
- **Unsupported / compatibility:** the result does not certify semantic parity for every external
  database, transaction shape, query expression, tenancy mode, or migration path. Provider-bounded
  streams do not imply snapshot consistency, mutation-safe iteration, resumability, or constant memory
  inside opaque drivers. The surface is pre-1.0.
- **Maturity / safe claim:** `verified`. Core `Entity<T>` persistence semantics are automated and are
  Koan's strongest first-class application language; provider-specific claims require their suites.
- **Current T6 boundary:** SQLite is the durable Level-1 application provider (35/35 plus both
  executable journeys); InMemory is the ephemeral conformance oracle (56/56); JSON is the bundled
  zero-infrastructure fallback (20/20), not a durable application claim. The canonical
  [Data foundation reference](../../reference/data/index.md) publishes selection, inspection, and
  unsupported scenarios without promoting public package maturity.
- **Current R07-01 evidence:** R07-01 passes after migrating Data, Tenancy, Access, Jobs, and Data.AI to
  the Core-owned context seam and removing Data's generic slice/carrier APIs. The affected matrix
  proves fail-closed durable restoration and context-isolated embedding queue identity. Safe carrier
  descriptors are code-inspectable; startup/runtime-fact projection remains deferred. This is an
  ownership repair, not a maturity promotion or a Communication claim.
- **Current R07-02 evidence:** the focused Data.Core streaming surface passes 42/42 and Data.Core full
  passes 325/325, including bounded
  page requests, first-yield laziness, no count, cancellation/disposal, residual continuation,
  total-order/overclaim rejection, natural cancellation overloads, selected/rejected runtime facts,
  and stable routed source, partition, and registered carrier context for one enumeration. SQLite's
  focused provider proof passes 1/1. The shared realization cell passes once each for SQLite,
  PostgreSQL, CockroachDB, SQL Server, MongoDB, and Couchbase; its fail-closed branch passes once each
  for InMemory, JSON, and Redis. Every qualified cell proves the initial caller-sort floor—top-level
  non-nullable `bool`, `byte`, `sbyte`, `short`, `ushort`, and `int`—while only the usual string Entity
  id is admitted as an opaque provider-stable tie-breaker. The real Backup consumer passes 5/5 acceptance and 7/7
  full over SQLite pages `2/2/1`, caller cancellation, and InMemory/JSON rejection before query or
  archive publication. This is an adapter-bounded streaming claim, not a universal provider,
  cross-provider collation, or total-memory claim; R07-02 passes without promoting this row.
- **Open risks:** graduate the defined Entity semantic locations ring by ring; provider parity,
  concurrency, schema evolution, and compatibility remain separate claims.

### Backend discovery and negotiation

- **Outcome and shortest path:** reference a connector, optionally select it by configuration or
  attribute, and inspect its declared capabilities before invoking provider-specific behavior.
- **Entry point and owner:** [`AdapterResolver`](../../../src/Koan.Data.Core/AdapterResolver.cs),
  [`DataAdapterAttribute`](../../../src/Koan.Data.Abstractions/DataAdapterAttribute.cs),
  [`DataCaps`](../../../src/Koan.Data.Abstractions/Capabilities/DataCaps.cs), and core
  [`CapabilitySet`](../../../src/Koan.Core/Capabilities/CapabilitySet.cs).
- **Executable evidence:** Data.Core's last complete baseline passes 349/349. The R07-13 Relationships
  matrix passes 10/10 and Entity Language passes 22/22; the new cells prove inferred scalar/set/stream
  and custom-key grammar, parent/child batching, strict/bounded facts, and corrective cross-key
  rejection. Existing relationship cells execute InMemory, JSON, and SQLite selection/rejection/bounds;
  Web relationship passes 7/7; MCP relationship passes 2/2 and MCP
  conformance 74/74. R07-02 adds a 42/42 focused stream coordinator proof, one focused SQLite provider
  proof, six shared provider-bounded realization cells, three shared fail-closed cells, and a 5/5
  acceptance plus 7/7 full real Backup consumer proof. [`S10.DevPortal`](../../../samples/S10.DevPortal/README.md) demonstrates provider switching.
- **Inspection and failure:** [`AdapterResolutionDecision`](../../../src/Koan.Data.Core/Routing/AdapterResolutionDecision.cs)
  is the single calculation for configured/default data selection used by runtime behavior, lockfile,
  and schema-1 facts. [ARCH-0112](../../decisions/ARCH-0112-bounded-relationship-negotiation.md) adds
  physical filter profiles and corrective relationship rejections. R07-02 adds the
  `query.paging.providerBounded` capability, a corrective `QueryStreamRejectedException`, and safe
  selected/rejected per-Entity runtime facts without claiming a lazy repository election at boot.
- **Unsupported / compatibility:** R02 did not run every relational, document, cache, vector, or
  messaging connector, nor prove fallback/election behavior for every ambiguous multi-provider graph.
  InMemory, JSON, and Redis deliberately reject provider-bounded Entity streams because their current
  query path scans/materializes the complete source before slicing.
- **Maturity / safe claim:** `demonstrated`. Koan has a real capability-negotiation model and reports
  major elections; only specifically tested provider combinations may claim parity.
- **Open risks:** expand the stream sort floor only with new cross-provider proof; verify index
  sufficiency and performance; design recursive graph/depth budgets without turning
  facts into request history.

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

### Entity Events and Transport

- **Outcome and shortest path:** reference `Sylin.Koan`, call `AddKoan()`, declare typed business
  handlers, then use `entity.Events.Raise<TEvent>()` or `entity.Transport.Send()`. Scalar, set, and
  lazy-stream sources use the same pointwise meaning. No external adapter is required for the complete
  local ring.
- **Entry point and owner:** [`Koan.Communication`](../../../src/Koan.Communication/README.md) owns both
  semantic lanes and its minimum-priority process-local provider. A direct
  [`RabbitMQ Communication connector`](../../../src/Connectors/Communication/RabbitMq/README.md)
  reference elects RabbitMQ for Transport and internal framework routes without changing application
  code; Events stay local. Jobs wake uses competing groups; Cache peer invalidation uses every-node delivery.
- **Executable evidence:** Communication passes 37/37 local semantic/election/channel tests; Jobs passes
  82/82 including local signal/coalescing and current pointwise submission behavior. The connector's real RabbitMQ container
  passes 9/9 for direct-intent and named-channel election, confirmed persistent publication,
  two-group isolated fan-out, authenticated tenant restoration, mandatory no-route failure, boot
  facts, elected health, and zero-configuration orchestration intent.
- **Inspection and failure:** startup/operator/agent facts report lane provider, reason, priority,
  assurance, settlement observability, groups, bounds, and context carriage. An elected connector is
  critical to health; unavailable direct intent never falls back to local reach.
- **Supported channel policy:** the default stays configuration-free. Startup-declared business
  channels can pin Transport and Events independently, use the same terminal grammar, bind every
  typed group once, and report exact provider/assurance/binding facts. They are route policies, not
  authorization or receiver filters.
- **Unsupported / compatibility:** RabbitMQ Events, dynamic channels, automatic branching,
  mirroring/failover, retry, dedupe,
  inbox/outbox, dead letters, replay, schema negotiation, remote settlement, exactly-once effects, and
  application-authored framework signals are not supported. Legacy `Koan.Messaging` is not the
  implementation behind Entity Communication.
- **Maturity / safe claim:** `verified` for the process-local ring and the named RabbitMQ Transport
  guarantees above. No broader production-mesh or provider-parity claim follows.
- **Open risks:** add compatibility aliases/version negotiation before heterogeneous deployments;
  provider-specific broadcast durability and replay require separate proof.

### Jobs and scheduling

- **Outcome and shortest path:** implement `IKoanJob<T>` on a job entity and use the same entity/data
  language for execution, state, progress, retries, and scheduling.
- **Entry point and owner:** [`IKoanJob`](../../../src/Koan.Jobs/IKoanJob.cs) and
  [`JobsServiceCollectionExtensions`](../../../src/Koan.Jobs/JobsServiceCollectionExtensions.cs) in
  `Sylin.Koan.Jobs`.
- **Executable evidence:** the current core/in-process Jobs project passes 82/82. Its pointwise source
  cells prove async one-pass backpressure and multiplicity, explicit coalescing, typed source-failure
  and cancellation prefixes, and the shared finite behavior. Entity Language passes 25/25 for
  scalar/set/stream presence, absence, removal, and invalid receivers. The last complete SQLite
  baseline remains 79/79; R07-14 adds a focused 2/2 SQLite transaction/shared-source proof and a
  focused 1/1 tenant-context seal. [`S14.AdapterBench`](../../../samples/S14.AdapterBench/Jobs/BenchmarkJob.cs)
  demonstrates a business-facing job, while [`S6.SnapVault`](../../../samples/S6.SnapVault/README.md)
  now consumes the ledger-confirmed source summary. [`GoldenJourney`](../../../samples/GoldenJourney/README.md)
  observes completed business priority and 100% progress from a running source application.
- **Inspection and failure:** health snapshots/contributors, metrics, state transitions, explicit
  ledgers, and Communication-owned wake facts exist. Source submission returns a fixed-size
  acceptance summary; typed failure/cancellation preserve the confirmed prefix. Handler failures are
  recorded in job state rather than hidden in application glue.
- **Unsupported / compatibility:** R02 did not certify distributed competing consumers, messaging
  transport, every durable ledger, clock-skew behavior, or upgrade compatibility.
- **Maturity / safe claim:** `verified`. In-process/core job semantics are automated; distributed and
  provider-specific tiers must be claimed separately.
- **Open risks:** distributed ledger tiers still need their own current composition and behavior
  evidence; source submission is intentionally sequential/non-atomic, and streaming bounds producer
  memory rather than the cost of one ledger row per Entity.

### Cache and distributed state

- **Outcome and shortest path:** add cache intent through `Sylin.Koan.Cache` and entity-centered
  attributes/extensions; Koan resolves stores, policy, tiers, and coherence.
- **Entry point and owner:** [`CacheableAttribute`](../../../src/Koan.Cache.Abstractions/Policies/CacheableAttribute.cs),
  the module-owned [`EntityCacheFacet`](../../../src/Koan.Cache/Entity/EntityCacheFacet.cs),
  [`EntityCacheEntryFacet`](../../../src/Koan.Cache/Entity/EntityCacheEntryFacet.cs), the host-owned
  [`EntityCachePlan`](../../../src/Koan.Cache/Entity/EntityCachePlan.cs), typed stores,
  and the Cache-owned peer-invalidation coordinator over Communication.
- **Executable evidence:** the
  [cross-engine suite](../../../tests/Suites/Cache/CrossEngine/Koan.Tests.Cache.CrossEngine/Koan.Tests.Cache.CrossEngine.csproj)
  passes 14/14; the
  [Entity-language consumer suite](../../../tests/Suites/EntityLanguage/Koan.Tests.EntityLanguage/Koan.Tests.EntityLanguage.csproj)
  proves module absence/presence/removal, scalar/set/stream receiver validity, collision safety,
  explanation, control-plane operations, and corrective failure. Cache topology proves one shared
  repository/eviction plan, bounded execution, captured context, and partial outcomes. Cache Abstractions pass
  51/51, topology passes its current focused suite, analyzer passes 6/6, and Communication passes 33/33. Real Redis passes
  5/5 and real RabbitMQ passes 7/7, including every-node carriage.
- **Inspection and failure:** composition facts and health report topology, coherence mode, elected carrier,
  assurance, L1-only receipt, origin filtering, and the L1-TTL safety bound. Invalid provider pins fail loud.
- **Unsupported / compatibility:** no durable replay/catch-up, retry, dedupe, multi-carrier publication,
  remote settlement, global flush wire contract, or batch-atomic source eviction is claimed. The former
  `Uncache` and generic cache handle have no compatibility alias.
- **Maturity / safe claim:** `verified`. The tested cache contract and cross-engine semantics pass;
  production coherence guarantees remain topology- and provider-specific.
- **Open risks:** production guarantees remain provider/topology-specific; durable replay requires a real
  use case and provider contract before the surface grows.

### Media processing and serving

- **Outcome and shortest path:** in a Koan web app with Data and Storage providers, reference
  `Sylin.Koan.Media.Web`, derive one `MediaEntity<TEntity>`, declare a `[MediaRecipe]`, and call
  `AddMediaSource<TEntity>()`; Koan serves `/media/{id}/{recipe}` without an application rendering controller.
- **Entry point and owner:** [`MediaEntity<TEntity>`](../../../src/Koan.Media.Abstractions/Model/MediaEntity.cs),
  [`MediaRecipe`](../../../src/Koan.Media.Abstractions/Recipes/MediaRecipe.cs), the
  [Core registry/pipeline](../../../src/Koan.Media.Core/README.md), and the
  [Web source/controller](../../../src/Koan.Media.Web/README.md).
- **Executable evidence:** Media Core passes 562/562 across recipe grammar, pipeline, formats,
  negotiation, limits, derivative persistence, and failure paths. The real hosted Media Web suite passes
  4/4 for Entity access gating, persisted derivative round-trip, code/config recipe startup facts, and
  invalid-configuration boot failure. The maintained photo sample exercises on-demand HTTP rendering,
  direct in-process rendering, and targeted source-deletion cleanup.
- **Inspection and failure:** `KoanModule.Start` materializes recipes before traffic; invalid declarations
  stop host startup. Shared runtime facts and HTTP recipe endpoints read the same registry and report recipe
  source, version, fingerprint, steps, mutators, formats, and producible shortcuts.
- **Unsupported / compatibility:** no upload-time prewarm, scheduled orphan cleanup, automatic multi-source
  routing, signed/content-addressed Media route, configurable route prefix, or scalar/set/stream Entity Media
  facet is claimed. The stream `Store` path and default derivative write buffer their complete payloads.
- **Maturity / safe claim:** `verified`. The tested in-process recipe and Entity-backed HTTP contract is
  automated; lifecycle automation and broader routing remain explicitly unsupported, and the packages remain
  pre-1.0/unpublished through the current release process.
- **Open risks:** replace public `MediaDerivation` leakage and application cleanup only when one context-aware
  lifecycle/rendering coordinator has real consumers; do not add a facet or provider SPI for symmetry.

### AI, vector, and semantic capabilities

- **Outcome and shortest path:** annotate or extend an entity for embeddings and semantic search while
  choosing AI/vector connectors by reference and configuration.
- **Entry point and owner:** `Sylin.Koan.Data.AI`, `Sylin.Koan.Data.Vector`, and connector packages;
  vector capability vocabulary starts at [`VectorCaps`](../../../src/Koan.Data.Vector.Abstractions/Capabilities/VectorCaps.cs).
- **Executable evidence:** [AI unit tests](../../../tests/Suites/AI/Unit/Koan.Tests.AI.Unit/Koan.Tests.AI.Unit.csproj)
  pass 157/157 and the
  [in-memory vector surface](../../../tests/Suites/Data/VectorAdapterSurface/Koan.Data.VectorAdapterSurface.InMemory.Tests/Koan.Data.VectorAdapterSurface.InMemory.Tests.csproj)
  passes 33/33. [Data/AI integration](../../../tests/Suites/Data/AI/Koan.Data.AI.Tests/Koan.Data.AI.Tests.csproj)
  passes 84/84 after the R04-02 repeated-host repairs and the R07-01 context-isolated queue proof.
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
  passes 74/74, including custom-tool projection, caller-specific `koan://self` behavior, quiet
  convention-based schemas, and discoverable custom-mutation `dry_run` alongside
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
  split into bounded 17/17 Fast, 16/16 Pillars, and 7/7 Infrastructure lanes. The 16/16 packaging suite
  includes serialized isolated source-checkout FirstUse and GoldenJourney process proofs.
- **Inspection and failure:** capability-gated conformance can explain why some batteries do not apply;
  bounded runners report the lane, phase, command, deadline, and captured diagnostics on failure.
- **Current T6 change:** `EntityConformanceSpecs<T>` now enters its creating host's existing
  `AppHost.PushScope` for reachability and every inherited battery. The same-Entity concurrency proof
  fails on the old global behavior and passes after the repair; consumers no longer disable xUnit
  parallelization for the assembly.
- **Unsupported / compatibility:** inherited testing does not presently guarantee every entity/module
  combination, external infrastructure lifecycle, deterministic full-suite execution, or stable public
  test-kit APIs.
- **Maturity / safe claim:** `demonstrated`. Koan provides useful reusable conformance tests; applications
  do not yet inherit a fully verified, package-installable test contract.
- **R06 graduation result:** the testing portion of the foundation ring was internally verified: the
  parallel-enabled meta-suite passes 12 with 3 explicit capability/trait skips, and local connector
  suites pass InMemory 55/55, SQLite 15/15, and JSON 14/14. The row remains `demonstrated` because the
  public package path and stable test-kit compatibility boundary are not yet established.
- **Open risks:** publish the foundation ring's exact support boundary; keep every skipped capability
  reason explicit; consolidate provider-specific runners without accidentally starting infrastructure
  from routine solution tests.

### Packaging, installation, and upgrades

- **Outcome and intended path:** create a new web project, add coherent `Sylin.Koan.*` packages, call
  `AddKoan()`, and reach the first entity/API without cloning framework source.
- **Entry point and owner:** repository-wide package metadata in
  [`Directory.Build.props`](../../../Directory.Build.props), compatibility ranges in
  [`build/compat-ranges.targets`](../../../build/compat-ranges.targets), and NBGV
  [`version.json`](../../../version.json).
- **Executable evidence:** the release compiler inventories 112 independently versioned owners after
  deleting the two cache-specific coherence packages. Focused package-lineage tests pass 28/28 for
  permanent no-artifact retirement in both established and first-projection histories. A
  fresh Git-derived rehearsal selected and verified 84 packages: 45 changed versions and 39
  unpublished-current registry repairs. Its package-only clean room copied the exact public
  [`FirstUse`](../../../samples/FirstUse/FirstUse.csproj) and
  [`GoldenJourney`](../../../samples/GoldenJourney/GoldenJourney.csproj) apps outside the checkout,
  hydrated one local feed, and passed FirstUse 8/8 in 4.129s plus GoldenJourney 11/11 in 8.769s. Both
  external restores/builds emitted zero warnings and zero errors. An earlier exact-0.17.0
  public-NuGet probe also failed restore, but its dependency-specific diagnosis is historical and is
  not treated as current registry metadata. The maintained boundary is the one current evidence
  proves: the coherent set has passed staged clean rooms and has not yet passed an observed public
  publication clean room.
- **Current R05 change:** the fresh package proof embeds one source commit across independently
  selected App, SQLite, Jobs, and MCP versions and writes separate evidence for both applications.
  This proves local artifact coherence; it is not a public package-path claim because the release set
  was not published. A later lockfile-focused rehearsal at `a2780672` verified another 84-package
  closure: Core carried its composition target through `buildTransitive`, and external FirstUse plus
  GoldenJourney both emitted and validated checked-in lockfiles while passing their complete 8-step
  and 11-step contracts with zero build warnings/errors. Independent readers then reproduced the
  supported path, generated two bounded repair queues, and verified the affected contracts. The
  maintainer accepted the resulting evidence without promoting unpublished packages.
- **Current R07 result:** automatic lineage now distinguishes developer `SourceCommit` from the exact
  package `VersionCommit`, detects breaking version intent, and mints the evaluated transitive reverse
  closure without an operator list. The 52/52 packaging suite covers all-owner bootstrap, stored exact
  identity inventory, mapped shared-input fan-out, and canonical current version intent. In a
  disposable complete-repository rehearsal, a Data.Core 0.18 intent selected all 81 affected packages and
  generated 78 markers; registry reconciliation added 19 existing publication gaps. The resulting
  100 exact artifacts passed inspection and package-only FirstUse (4.095s) plus GoldenJourney
  (10.591s). No package was published, so this strengthens internal release evidence without
  promoting public maturity.
- **Inspection and failure:** the restore fails loudly, which is safer than an ABI mismatch, but the
  public docs previously described packages only as lagging and offered a broken copy/paste path.
- **Unsupported / compatibility:** the staged package set is coherent, but public package-first install,
  upgrades, rollback, migration, release cadence, and support windows are not established until the
  automated `dev` release is actually published and observed.
- **Maturity / safe claim:** `specified`. Staged PackageReference clean rooms are demonstrated, but
  those packages were not published; source checkout remains the only coherent path currently
  available to a new user.
- **Open risks:** observe the first trusted `dev` publication, prove cross-event partial-publication
  recovery, and retain prior maps for arbitrary external pack inputs; retain advisory review and
  establish explicit pre-1.0 upgrade/rollback policy before promoting the package path.

### Operations, health, and diagnostics

- **Outcome and shortest path:** inject `IKoanRuntimeFacts`, inspect boot output/health, request the
  gated `/.well-known/Koan/facts` endpoint, or read `koan://facts` to understand the decisions in the
  migrated runtime slice.
- **Entry point and owner:** [`IKoanRuntimeFacts`](../../../src/Koan.Core/Diagnostics/IKoanRuntimeFacts.cs)
  and core health under [`Koan.Core/Observability`](../../../src/Koan.Core/Observability), plus
  [`Sylin.Koan.Observability`](../../../src/Koan.Observability/Koan.Observability.csproj) and module
  contributors.
- **Executable evidence:** focused facts pass 7/7; Core 211/211, Data.Core 349/349, Web WellKnown 3/3,
  Web relationship 7/7, MCP relationship 2/2, and MCP conformance 74/74. These prove ordering, schema round-trip, redaction, host isolation,
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
