---
type: SPEC
domain: framework
title: "R12-02 - Close Preview-Blocking Seams"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-19
  status: in-progress
  scope: current-state PMC triage and dependency-ordered public-contract corrections
---

# R12-02 — Close preview-blocking seams

- Tranche: `T7C — 0.20 public-preview maturity`
- Status: `in-progress`
- Depends on: passed R12-01 selective preview/version contract
- Unlocks: an evidence-derived preview product boundary in R12-03
- Owner: fix, removal, or explicit exclusion of concerns that would make a proposed 0.20 guarantee false

## Meaningful outcome

A package is not withheld from 0.20 because an old assessment merely mentions it, and it is not
promoted while a current promise-level defect remains. Every PMC has one current disposition tied to
the accepted guarantee boundary. Repairs happen at the narrow owner of meaning; nonclaims remain
truthful; already-repaired history is closed by evidence rather than rebuilt.

## Current-state guarantee-impact map

This map is the initial triage. `repair` authorizes focused exploration, not a predetermined code
change. `phase` transfers a required proof to the child that owns its real terminal. `exclude` means
the concern does not enter the stated 0.20 guarantee; it does not mean the package or idea is broken.

| PMCs | Current disposition | 0.20 consequence |
|---|---|---|
| 001 | repaired and closed | The framework-owned row is internal with unchanged persistence identity; one public `JobMetrics.Summary(...)` operation replaces the accidental Entity surface. |
| 002, 004 | repaired and closed | Host-level explicit transports replace transitional/per-Entity selection; one camelCase application-payload contract spans Entity, custom tools, and Code Mode. |
| 003, 028, 032 | close by current evidence | R11-07 built the complete Release solution with zero warnings and ran the SQLite/connector projects; the historical warning and missing-reference premises no longer hold. |
| 005 | exclude from supported workflow | Linked-worktree release execution is convenience, not the supported clean-checkout release path. |
| 006, 020 | phase to R12-06 | Live bounded progress and retained aggregate certification evidence belong to the actual publication operator path. |
| 007, 015 | repaired and closed | The current filter AST/coordinator already provides execute-or-reject semantics; shared Web proofs and current docs now state it. Data's first-use shape guard rejects case-colliding Entity properties before adapter creation. |
| 008, 018 | exclude with non-promoted providers | Vector transaction reflection and remote provider provenance do not enter the initial local Data guarantee. |
| 009 | phase to R12-04 | The coherent-public-narrative gate owns public XML-doc/link enforcement. |
| 013, 014 | exclude with Backup | Backup remains outside the initial supported boundary; metadata-only encryption and incomplete Web operations remain explicit limits. |
| 021 | exclude from Communication guarantee | Runtime rejection for missing required event details is supported; a compile-time analyzer is not promised. |
| 022, 027 | withhold Media/Storage group pending R12-03 | Derivative lifecycle and connector-owned evidence are not silently inherited from Media's verified pipeline tests. |
| 023 | explicit Communication nonclaim | One-application RabbitMQ carriage is eligible; heterogeneous schema/rename evolution is not promised. |
| 024 | re-audit in R12-02 | Release/build fixture isolation is safety-relevant and may have been superseded by R08/R11 repairs. |
| 025 | closed by current evidence | Current source/package FirstUse uses no EventLog override and passes on Windows; .NET 10.0.8 degrades EventLog `SecurityException` without taking down console logging. Koan does not replace standard host provider ownership. |
| 026 | exclude from guarantee | Conservative analyzer impact is release-safe; output-sensitive optimization is not a preview contract. |
| 029 | phase to R12-05 | The lifecycle fix is already implemented; the next exact package-only consumer must close its remaining observation gate. |
| 030 | exclude with AI | Mixed adapter disposal ownership remains outside the initial supported package slate. |
| 033 | repaired and closed | GardenCoop C2 now passes 1/1 without Storage in its graph. The remaining generic invariant is fixed at Storage's chokepoint: availability is inert, while configuration or actual service use activates one fail-loud routing plan. |
| 034, 035 | explicit Auth/Tenancy nonclaims | Password/MFA ceremony and distributed invitation acceptance remain absent rather than falsely partial. |

## First chokepoint exploration — Storage layered activation

**Task:** Determine whether current transitive Media → Storage availability incorrectly activates
Storage routing, and if so make configuration/operation intent—not assembly presence alone—the plan
materialization boundary.

**Application intent:** An application may use Media's in-memory recipe/pipeline features without
configuring blob storage. When it configures or actually invokes Storage, Koan must compile the route
once or reject with the existing corrective profile/provider message.

**Public expression:** Reference the desired Media or Storage package, call existing `AddKoan()`, and
use existing Media/Storage APIs. No new enable flag, attribute, registrar call, or application service
is introduced.

**Guarantee/correction:** An unconfigured, unused Storage runtime is available but inactive and does
not stop the host. Declaring a Storage profile validates at startup. Resolving/using the standard
Storage service without a profile fails with the existing `Koan Storage has no profiles` correction.

**Complete intent surface:** package reference; optional `Koan:Storage:Profiles` and
`DefaultProfile`; provider references; `AddKoan()` host start; runtime-fact projection;
`IStorageService` resolution; `StorageEntity<T>` and Media storage-backed operations.

**Public concepts:** Existing standard configuration, DI resolution, package references, and runtime
facts only.

**Docs read:** `CLAUDE.md`, `NOW.md`, R12/R12-01, the complete PMC register, capability maturity
definitions, generated product surface, R11-05 Storage/Media findings, and Storage/Media package
README/TECHNICAL contracts.

**Code read:** `StorageModule`, `StorageRoutingPlan`, `StorageCompositionFacts`, `StorageOptions`,
`StorageService`, `MediaCoreModule`, `MediaCompositionFacts`, Media's Storage-based Entity/extensions,
Media Web startup fixtures, Storage routing/bootstrap specs, and the current GardenCoop C2 fixture.
Repository-wide constant/options/request/response discovery searches were completed before placement.

**Reusing:** `StorageOptions` remains the configuration owner; the DI-owned singleton
`StorageRoutingPlan` remains the one compiler and corrective failure owner; `StorageCompositionFacts`
remains the inspection owner; existing Media and Storage host tests remain the consumer oracles.

**Creating new:** No new production abstraction. Add only focused host tests for inactive availability,
configured startup validation, runtime correction, and facts if the current defect reproduces.

**Coalescence:** Keep availability and activation in `StorageModule`/`StorageCompositionFacts`. Do not
add `StorageEnabled`, an activation registry, a Media exception, or sample-only configuration.

**Ergonomics:** Pipeline-only Media users configure nothing. Storage users keep the same profile
configuration and receive the same corrective error at the earliest point their intent becomes real.

**Constraints satisfied:** business intent inward; standard DI/options/configuration; one pillar
chokepoint; no model decoration; no cross-module activation contract; no full ratchet; no remote or
private application access.

**Risks:** Deferring route compilation could hide invalid configuration, make facts resolve a service
accidentally, or weaken reference-as-intent. The implementation must still compile eagerly whenever
profile/default intent exists, keep runtime failure for unconfigured use, and distinguish available
from active without resolving the route.

## Focused proof for the first slice

- current GardenCoop C2 remains green and is recorded as historical-trigger closure, not repair proof;
- a real `AddKoan()` Media host with the transitive Storage runtime and no profile starts;
- its facts report Storage providers/availability without a selected route;
- resolving standard `IStorageService` without a profile fails correctively;
- configured Storage still compiles at startup and existing route/fact tests remain green;
- affected Storage, Media Web, and Data.AI tests only; no complete release ratchet.

## First slice outcome

PMC-033 is closed without a new public concept. `StorageOptions` now answers whether routing intent is
declared; `StorageModule` eagerly compiles only declared configuration; actual `IStorageService`
resolution retains the same corrective no-profile failure; and `StorageCompositionFacts` reports
inactive availability without resolving a route. Focused Release evidence passes Media Web 8/8,
Storage Core 20/20, bootstrap pillars 13/13, Data.AI 87/87, and GardenCoop C2 1/1. Configured Media/Storage
paths remain covered by the same suites. No full ratchet or remote action ran.

PMC-025 is also closed without production change. The current application probe contains no EventLog
override; the exact R08-05 candidate, R11-07 public contract, and a fresh source FirstUse proof pass on
Windows. Inspection of the exact Microsoft.Extensions.Logging.EventLog 10.0.8 binary confirms that a
`SecurityException` disables only that sink. Koan therefore retains standard host logging ownership
instead of removing user providers or adding a Koan-specific toggle for a stale premise.

The next dependency-ordered exploration is PMC-001's Jobs Entity-language collision. MCP public shape
corrections follow; Data/Web parity and model identity follow their current convergence audit.

## Second chokepoint exploration — Jobs metrics boundary

**Task:** Remove the `JobMetric.Count` collision from Jobs' 0.20 public Entity language without
inventing a migration, alias window, or analyzer subsystem for framework-owned persistence mechanics.

**Application intent:** An operator asks Jobs for retained throughput totals by work type, time range,
and outcome. Applications do not author, save, or query framework metric rows as business Entities.

**Public expression:** `await JobMetrics.Summary(workType, from, to)`. This is one operation-oriented
concept; the persisted shard row is not public application vocabulary.

**Guarantee/correction:** The summary continues to aggregate node shards and survive JobRecord
retention. The existing storage type and field identity remain unchanged. Direct public `JobMetric`
Entity access is removed before the 0.20 guarantee rather than preserved as a misleading compatibility
surface.

**Complete intent surface:** `JobsOptions.MetricsEnabled`, per-node recording/flush, metric retention,
the summary query, Tenancy ambient exemption, Jobs package docs/guides, and exported CLR surface.

**Public concepts:** One plural standard .NET static API class, `JobMetrics`; existing options and
return types. No new Entity facet, attribute, configuration, DTO, service, or analyzer.

**Docs read:** PMC-001, Jobs README/TECHNICAL, Jobs how-to/framework-utilities guidance, generated
product surface, and the R11 Jobs graduation evidence.

**Code read:** `JobMetric`, `JobMetricsRecorder`, `JobOrchestrator`, `JobsOptions`, Jobs project friend
boundaries, behavior harness/suite, compatibility surface specs, Tenancy ledger-exemption spec,
`Entity<T>.Count`, and Data projection/storage-name resolution.

**Reusing:** Keep the existing `JobMetric` CLR name, `Count` field, Entity persistence, shard key,
retention, and summary query internally so stored data does not migrate. Reuse the existing focused
Jobs behavior and Tenancy isolation suites.

**Creating new:** One public static `JobMetrics` facade containing the already-public summary
operation. No other production type or mechanism.

**Coalescence:** Internalize the existing row instead of renaming its persisted fields or teaching
users a framework ledger Entity. Do not add a compatibility alias or a general analyzer without a
current application-level collision corpus.

**Ergonomics:** The public name describes the question, not its storage. `JobMetrics.Summary(...)`
reads as operator intent and cannot shadow `Entity<T>.Count`.

**Constraints satisfied:** fewer public moving parts; Entity remains application language rather
than framework storage leakage; standard static API; unchanged persistence; no cross-module contract;
focused tests only.

**Risks:** An undocumented consumer may have queried `JobMetric` directly. That surface is deliberately
broken before 0.20 because its public persistence shape was not an accepted guarantee. Reflection and
generated-surface tests must prove the row is no longer exported, while behavior proves retained totals
and Tenancy exemption remain intact.

## Second slice outcome

PMC-001 is closed by making the architecture match the application sentence. `JobMetric` remains the
same internal Entity and retains its type/field persistence identity; `JobMetrics.Summary(...)` is the
one exported operation. Package guidance now teaches that API and explicitly describes metrics as
derived and lossy-tolerant. Jobs passes 84/84, Tenancy passes 16/16, and the Jobs Release build has zero
warnings/errors. No general analyzer was added because the current defect was framework storage leakage,
not evidence for another public toolchain.

## Third chokepoint exploration — MCP transport and application JSON contract

**Task:** Replace MCP's transitional HTTP/SSE option vocabulary and split application serializers
with one pre-0.20 host transport contract and one application-payload JSON contract.

**Application intent:** An application chooses which MCP edges the host exposes, chooses one HTTP base
route and session lifetime, and receives the same camel-case business payload whether a tool is
entity-derived, hand-written, reached through STDIO, or reached through HTTP.

**Public expression:** Configure `EnableStdioTransport`, `EnableStreamableHttpTransport`, optional
deprecated `EnableLegacySseTransport`, `HttpRoute`, and `SessionIdleTimeout` under `Koan:Mcp`. Mark a
business type once with `[McpEntity]` or expose a verb with `[McpTool]`; do not repeat host transport
choices on every entity.

**Guarantee/correction:** Streamable HTTP remains secure opt-in and legacy SSE remains explicit
deprecated opt-in. Both share the configured HTTP route and unified sessions. All enabled transports
project one capability surface and application payloads use camelCase while honoring `[JsonProperty]`
and directional `[McpIgnore]`. Protocol envelopes retain their protocol-owned names.

**Complete intent surface:** options binding; startup provenance; endpoint contribution; Explorer map
and route discovery; Streamable and legacy endpoints; session reclamation; capability reporting;
entity registry; custom-tool binding/results; Entity result/input translation; mutation deltas; Code
Mode object conversion; package/sample/guide configuration; and exported CLR surface.

**Public concepts:** Standard .NET options and JSON naming only. The host owns three explicit transport
switches and one HTTP route. `McpTransportMode` and per-entity `EnableStdio`/`EnableHttpSse` controls are
removed because transport availability is a host decision, not business-model metadata.

**Docs read:** PMC-002/004, R12/R12-01/R12-02, MCP package README/TECHNICAL, the current MCP-over-HTTP,
OAuth, agent-native, reference-card, sample, and case-study surfaces, plus accepted AI-0037 history.
Dated decisions, assessment logs, and archives remain history rather than current instructions.

**Code read:** `McpServerOptions`, configuration constants/provenance, endpoint contributor/mapping,
both HTTP transports, unified session manager, Explorer route owner, capability reporter, surface
projector, `McpEntityAttribute`/override/registration/registry/server, RPC handler/dispatcher,
`McpContractResolver`, `McpFieldPolicy`, request/response translators, mutation delta projector,
custom-tool invoker, Code Mode JSON facade, and focused conformance/Streamable fixtures.

**Reusing:** Keep the existing three transport implementations, one dispatcher, one session manager,
one entity registry, one access gate, Newtonsoft attributes, and the existing focused real-host suites.
Make `McpContractResolver` the shared application naming/exclusion policy rather than creating another
serializer framework.

**Creating new:** No public abstraction or compatibility layer. Add only golden wire assertions and
binding/provenance assertions at existing focused test owners; an internal serializer helper is allowed
only if it removes duplicated settings from the real payload paths.

**Coalescence:** Remove the transitional master-plus-nullable-override pair and old SSE-derived route
and timeout names. Remove per-model transport selection rather than renaming it: the host edge is the
single transport chokepoint, while the shared registry/gate owns tool visibility. Keep legacy SSE as a
thin opt-in transport, not a second capability model. Keep protocol-envelope serialization separate
from application-payload serialization because the MCP specification owns the former.

**Ergonomics:** A remote host says `EnableStreamableHttpTransport: true`; changing `/mcp` is
`HttpRoute`; session cleanup is `SessionIdleTimeout`. A tool author writes ordinary PascalCase C# and
clients see idiomatic camelCase JSON everywhere without annotations or transport-specific decoration.

**Constraints satisfied:** current defect and consumer evidence justify the break before 0.20; fewer
public moving parts; standard options/JSON concepts; one host transport owner and one payload policy;
no cross-module contract; focused tests only; no remote or private application access.

**Risks:** This deliberately breaks old pre-preview option/property names and removes public
per-entity transport flags. Configuration examples, Explorer discovery, provenance, and fixtures must
move atomically. Camel-casing can drift schemas, deltas, nested custom results, or Code Mode unless the
same resolver owns them. Legacy wire framing and MCP protocol DTO casing must remain byte/spec stable.

## Third slice outcome

PMC-002 and PMC-004 are closed as one boundary correction. A host now makes three explicit transport
decisions: STDIO, Streamable HTTP, and deprecated legacy SSE. `HttpRoute`, `MaxConcurrentSessions`,
and `SessionIdleTimeout` describe the shared HTTP/session core without lying about the primary
transport. The transitional master switch, nullable override, derived state, `McpTransportMode`, and
per-Entity transport flags are gone; custom-tool-only hosts are no longer rejected merely because
their registry contains no Entity. Startup provenance, Explorer projection, capability reporting,
samples, and current guidance use the same vocabulary.

`McpContractResolver` now owns camelCase plus directional `[McpIgnore]` for application data.
Entity schemas/inputs/results, custom-tool inputs/results, mutation deltas, and Code Mode reuse it;
JSON-RPC/MCP protocol DTOs retain their explicitly named protocol shape. Golden fixtures prove both
Entity and nested custom-tool payloads, canonical option binding, removed aliases, provenance,
capability reporting, Explorer projection, and field exclusion. Focused Release evidence passes MCP
conformance 80/80, Streamable plus legacy HTTP 19/19, field exclusion 5/5, Code Mode 27/27, and the
source FirstUse/GoldenJourney consumer boundary 3/3, and bootstrap pillars 13/13. The MCP Release build
has zero warnings/errors; public docs pass 233/42 and docs lint has zero errors.

The next dependency-ordered exploration is the current Data/Web filter and portable model-identity
boundary (PMC-007/015). Current provider convergence must be audited before choosing repair or
narrowing; no historical drop-filter premise is assumed current.

## Fourth chokepoint exploration — Data/Web query truth and portable Entity names

**Task:** Reconcile PMC-007 against the current unified filter pipeline, and close PMC-015 at the
earliest shared Entity-model boundary if case-colliding public properties still lack one portable
identity.

**Application intent:** A developer can submit one documented JSON filter through an Entity HTTP
endpoint and trust that Koan either evaluates it correctly or returns a corrective client error. The
same Entity model must have one stable public/property identity across Web JSON and every Data adapter.

**Public expression:** Use `?filter=<url-encoded-json>` or the existing POST query body against an
ordinary `Entity<T>`. Name each public Entity property uniquely without relying on case alone. No
provider switch, model attribute, filter mode, or mapping profile is introduced.

**Guarantee/correction:** Parsed filters are never silently dropped. The Data-owned coordinator pushes
only supported nodes, evaluates residual nodes centrally, sorts before paging, and Web maps malformed,
unknown, or unsupported input to 400. An Entity whose public instance properties collide under
ordinal case-insensitive comparison rejects on first repository request, before adapter selection or
provider I/O, with one rename correction.

**Complete intent surface:** GET `filter`; POST query bodies; hook-contributed access predicates;
filter parsing and field resolution; pushdown capability declarations; residual evaluation; sorting,
counting, and pagination; InMemory/JSON scan execution; native provider translation; Entity first-use
repository resolution; JSON/property naming; public Data/Web guidance; and provider convergence tests.

**Public concepts:** Existing JSON filter syntax, HTTP 400 errors, ordinary CLR property names, adapter
capabilities, and the existing Entity shape guard only.

**Docs read:** PMC-007/015, R12-01/R12-02, DATA-0096/0031 history, current Web HTTP reference, Entity
capabilities guide, and the shared adapter-surface/convergence test contracts.

**Code read:** `EntityEndpointService`, query-body parse regression specs, `JsonFilterParser`,
`FieldPathResolver`, `FilterSplitter`, `FilterPushdownCoordinator`, `IQueryRepository`, `Data<T>`,
`KeyValueStore`, InMemory/JSON repositories and capability declarations, native provider capability
derivations, `AggregateMetadata`, `ProjectionResolver`, `IdentityEncoding`, `DataService`, and
`EntityShapeGuard`. Repository-wide searches covered model validation, property projection,
serialization, and adapter-surface ownership.

**Reusing:** Keep the filter AST/coordinator as the single execution owner and the shared Web adapter
surface plus Data filter-convergence oracle as evidence. Extend the existing cached Entity shape guard,
which already runs before adapter resolution, instead of adding provider validation.

**Creating new:** No production abstraction. Add one model-rule branch and focused guard/first-use
tests. Correct current Web/Entity guidance to state the actual execute-or-reject contract without
claiming universal native pushdown or equal cost.

**Coalescence:** PMC-007 needs evidence and a documentation correction, not another filter layer.
PMC-015 belongs beside the existing concrete-inheritance shape rule because both are universal Entity
validity constraints. Do not add case-sensitivity capabilities, persisted-name annotations, serializer
exceptions, or per-adapter checks.

**Ergonomics:** Normal PascalCase models and camelCase clients keep working, including unambiguous
case-insensitive field input. A problematic `Id`/`id` model fails once with both property names and a
plain rename instruction, before a database or file is touched.

**Constraints satisfied:** business intent inward; fewer moving parts; standard CLR naming and HTTP
errors; genuinely Data-owned cross-provider semantics; no model decoration; focused local tests only;
no remote/private application access or release ratchet.

**Risks:** A pre-preview application may intentionally expose properties distinguished only by case on
a case-sensitive backend. That shape is deliberately rejected because it cannot survive the complete
Web/Data adapter boundary. Reflection must not mistake a normal override for a collision, validation
must precede factory creation, and the docs must distinguish semantic parity from native pushdown and
performance parity.

## Fourth slice outcome

PMC-007 closes by current architecture plus strengthened evidence, not a second filtering subsystem.
Web parses GET and POST input into the shared filter AST; Data's coordinator pushes only adapter-owned
nodes, evaluates the residual centrally, and completes sort/pagination in semantic order. Malformed
JSON, unknown fields, and unsupported input remain corrective 400 responses. Current guidance now
promises result semantics without falsely promising equal native pushdown or provider cost. Shared
HTTP proofs cover equality, compound operators, mixed-case fields, malformed JSON, and unknown fields
across InMemory 74/74, JSON 52/52, and SQLite 52/52.

PMC-015 closes at the existing Data first-use shape guard. A public `Id`/`id` collision now rejects
with the Entity and both property names before adapter selection or creation; ordinary overrides and
unique property names remain valid. The guard is internal framework machinery, not a new application
concept. Exact guard/activation tests pass 9/9, and Data.Core plus the shared Web adapter test kit build
with zero warnings/errors. No provider capability, naming annotation, mapping profile, or compatibility
alias was introduced.

The next dependency-ordered exploration is PMC-024's build-fixture isolation invariant, followed by
current-evidence closure of PMC-003/028/032 where the R11 release record already suggests their
historical warning/reference premises are stale.

## Stop conditions

- Stop if a repair adds a second activation system or an application-facing mode knob.
- Stop if a historical PMC is treated as current without reproducing or locating its remaining invariant.
- Stop if an out-of-slate package is promoted merely to satisfy dependency closure.
- Stop before package version edits; R12-03 owns the exact admitted product boundary.
