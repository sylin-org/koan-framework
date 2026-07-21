---
type: SPEC
domain: framework
title: "R10-07 - Rebuild OrderIntake as the Provider Workload Lab"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: local receipt, optional-source activation, corrective failure, health participation, facts, build, and docs
---

# R10-07 — Rebuild OrderIntake as the provider workload lab

- Tranche: `T7B — active-sample graduation`
- Status: `passed`
- Depends on: R10-06 g1c2.GardenCoopEmbedded graduation
- Owner: honest named-source trials, durable Entity-first work, comparable receipts, and corrective optional infrastructure

## Task

Break and rebuild `applications/OrderIntake`. Retire the inherited premise that one synthetic dashboard can
rank unlike data products or choose an application's architecture. Preserve the valuable core: execute real Koan
Entity work durably, target deliberately named sources, expose the selected source's declared capabilities, and
leave an exact receipt that a developer or reviewer can compare responsibly.

## Application intent

“Run one bounded order-intake workload against a chosen configured source, verify that every order can be read
back, clean up only that run, and preserve a durable receipt describing exactly what happened.”

## Public expression

The host remains the canonical four lines:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

The application expression is one `TrialOrder : Entity<TrialOrder>`, one
`OrderIntakeTrial : Entity<OrderIntakeTrial>, IKoanJob<OrderIntakeTrial>`, and one controller that submits and
observes trials. A typed `WorkloadTarget` names application intent (`Local`, `Documents`, `Relational`, and
`KeyValue`); standard named-source configuration maps those intents to SQLite, MongoDB, PostgreSQL, and Redis.
The durable trial and Jobs ledger remain on the local default SQLite source. No service/repository, SignalR hub,
provider switch, benchmark engine, or application-owned database tuning is needed.

## Guarantee and correction

The zero-infrastructure path targets `Local`: a submitted trial writes a bounded batch, reads the exact IDs back,
verifies the business values, removes those IDs, completes durably, and exposes a receipt containing the source,
declared capabilities, counts, phase durations, and runtime fingerprint. The receipt is evidence for this workload
on this environment; it is not a universal provider ranking or architecture recommendation.

The three external targets are optional. Referencing their adapters describes available composition; it does not
make unreachable infrastructure healthy or gate the local path. When an optional source is used while unavailable,
the job must fail loudly, its status surface must give the exact compose command/correction, and readiness must then
report that selected dependency as unhealthy. Starting the pinned sample services makes the same workload available
without application code changes.

## Exploration evidence

### Current behavior and claims

- The project directly references four data adapters but does not declare named sources. Current priority election
  selected MongoDB as `data:default`, so the supposedly local benchmark and Jobs ledger depend on whichever
  discovered provider wins.
- `Program.cs` hard-codes a second SQLite connection string, opens SQLite directly, applies six application-owned
  performance PRAGMAs, catches any failure, then calls `RunAsync()` without awaiting it. Strict Release build fails
  on CS4014; the normal process starts, reports MongoDB as the default, and exits when top-level execution ends.
- SQLite receives durability/performance changes that other providers do not receive. A separate unused
  `SqlitePerformanceConfigurator` repeats more PRAGMAs and swallows every error. The resulting timings cannot support
  the README's “objective comparison” or provider-selection guidance.
- `BenchmarkService` is a 1,271-line synthetic engine with three technical Entity tiers, eleven operations per
  provider/tier, parallel provider contention, every ordered provider-pair migration, stringly adapter overrides,
  hand-computed progress, and asserted `UsedNativeExecution` values. It mixes capability demonstration,
  microbenchmarking, load/stress testing, data transfer, and architecture advice without a controlled methodology.
- `BenchmarkJob` manually constructs the service despite having scoped DI, fire-and-forgets durable progress,
  blocks async SignalR calls, and writes failures to `Console`. The controller retains a second synchronous engine,
  async-void progress, hard-coded “available” providers, and a second hard-coded tier catalog.
- The README says .NET 9 while the project targets .NET 10, documents inconsistent ports, requires all providers for
  local use, and promises native/fallback comparisons, historical trends, objective recommendations, Docker
  deployment, and 8–10 minute suites without repeatable current evidence. `DX-0044` and later docs repeat those
  stale claims and legacy APIs.

### Concepts retained and deleted

- Keep: parameterless `AddKoan()`, Entity operations, typed Jobs semantics, durable `ctx.Progress`, named sources,
  provider capability declarations, standard configuration, startup/facts/readiness, optional pinned compose
  services, dependency-free UI, and an exact HTTP request file.
- Create only: two business-facing Entities, one target enum, one compact receipt, one controller, a small safe DOM
  dashboard, focused cumulative proof, and the configuration/route constants those surfaces actually share.
- Delete: benchmark modes/scales/tiers, provider-pair migrations, raw adapter selection, SignalR, Chart.js,
  benchmark services/interfaces, direct SQLite access/tuning, manual provider catalog, synchronous endpoint,
  historical-results/export promises, application Docker image, start scripts, build-fix/improvement diaries, and
  universal ranking/advice.
- Coalescence: the job Entity owns its bounded business workflow; Koan Jobs owns execution/progress/persistence;
  named-source routing owns provider choice; adapters own connection and tuning; runtime facts own composition
  explanation. No sample abstraction duplicates those owners.
- Hot path: provider/source plans and job bindings compile once. Each trial resolves one named source, one capability
  snapshot, and performs three batch operations. No per-operation discovery, reflection, container probing, or
  provider negotiation is added.

## Code placement

| Change | Location | Reason |
|---|---|---|
| host, configuration, requests, UI | `samples/applications/OrderIntake/` | one obvious executable application |
| order and trial semantics | `samples/applications/OrderIntake/Domain/` | code reads as the business workload and receipt |
| submit/status/target projection | `samples/applications/OrderIntake/Controllers/` | custom HTTP remains controller-owned |
| optional services | `samples/applications/OrderIntake/docker/compose.yml` | infrastructure mechanics stay outside application code |
| cumulative contract | `tests/Suites/Samples/Koan.Samples.OrderIntake.Tests/` | real local host, durable job, receipt, facts, cleanup, and correction |
| public truth | sample README/index, DX-0044, Jobs card, R10/NOW/PROGRESS | no stale benchmark recommendation remains active |

## Implementation exploration contract

**Task:** replace OrderIntake's synthetic benchmark stack with one bounded, durable, provider-routed business
workload and a focused executable contract.

**Application intent:** “Run one bounded order-intake workload against a chosen configured source, verify that every
order can be read back, clean up only that run, and preserve a durable receipt describing exactly what happened.”

**Public expression:** the standard four-line `AddKoan()` host plus `TrialOrder : Entity<TrialOrder>` and
`OrderIntakeTrial : Entity<OrderIntakeTrial>, IKoanJob<OrderIntakeTrial>`. A caller submits
`POST /api/trials/{target}?count={boundedCount}` and polls the returned trial resource. Direct references make
SQLite, MongoDB, PostgreSQL, Redis, Web, and Jobs available; bundled configuration pins `Default` and `Local` to
SQLite and maps the three optional targets. Local requires only a writable sample directory.

**Guarantee/correction:** `Local` durably records the trial and Jobs ledger, upserts deterministic order IDs, reads
those exact IDs, verifies their business values, removes exactly those IDs, and persists counts, phase timings,
provider capabilities, and a runtime fingerprint. Every phase is idempotent under at-least-once job execution.
An unavailable optional target settles as a failed job whose status names the exact target-specific compose command.
Configuration alone does not gate readiness; selecting that route activates its readiness responsibility without
changing the SQLite default or corrupting the local control plane.

**Complete intent surface:** developers add no service, repository, worker, queue, provider selector, SignalR hub,
database tuning, or host middleware. The sample's references and source configuration are the complete infrastructure
intent; its Entity/job/controller code is the complete application intent. Operators optionally run one named
compose service before selecting a non-local target.

**Public concepts:**

- `WorkloadTarget` states the business-facing destination intent without exposing connector ids at the API.
- `TrialOrder` is the bounded business payload exercised through ordinary Entity batch verbs.
- `OrderIntakeTrial` is the durable job/work item and owns the idempotent three-phase workflow.
- `TrialReceipt` is the reviewable evidence produced by that workflow; it is not a ranking.
- `TrialStatusView` joins work-item result with Jobs lifecycle/progress for HTTP without duplicating orchestration.

**Docs read:**

- `docs/engineering/index.md` requires Entity-first data, controller-owned HTTP, centralized constants, and focused
  validation; it directly governs every replacement file.
- `docs/architecture/principles.md` requires business-to-code mapping, one composition, domain-owned meaning, thin
  runtime paths, and corrective semantic honesty; it rejects the current benchmark/provider-switch machinery.
- `docs/reference/cards/jobs.md` defines the canonical Entity + `IKoanJob<T>` grammar, durable progress, at-least-once
  idempotence, and ledger ownership used by the trial.
- `docs/guides/jobs-howto.md` confirms normal versus inline execution, durable ledger behavior, status/query APIs,
  retries, and progress semantics; the sample uses the ordinary normal worker while focused tests tune time only.
- `docs/getting-started/overview.md` establishes explicit source intent and four-line hosting as the greenfield path.

**Code read:**

- current `Program.cs`, `BenchmarkJob`, `BenchmarkController`, and `BenchmarkService` mix host wiring, direct SQLite
  tuning, duplicate engines, SignalR, hard-coded availability, and technical benchmark tiers; rebuild/delete.
- `samples/applications/DevPortal/Controllers/PublicationController.cs` is the closest named-source/corrective-error
  consumer; retain its narrow `EntityContext.Source` idea, not its publication-specific transfer workflow.
- `samples/GoldenJourney/Domain/ReviewRequest.cs` is the closest business job; retain the static handler and durable
  progress grammar.
- `src/Koan.Data.Core/Model/Entity.cs` already supplies bounded batch save/get/remove statics; no repository or new
  framework abstraction is required.
- `src/Koan.Jobs/JobCoordinator.cs`, `JobRecord`, and `JobContext` establish that work-item save, ledger progress,
  settlement, and captured context belong to Jobs. Target source scopes therefore wrap only TrialOrder operations,
  never the entire handler.

**Reusing:** standard named-source configuration and `EntityContext.Source`; Entity batch `Save`, `Get(ids)`, and
`Remove(ids)`; `Data<T>.Capabilities`; Jobs submission, worker, ledger query, and `ctx.Progress`; `KoanModule.Report`;
TestServer and the graduated sample fixture convention.

**Creating new:**

| New code | Location | Justification |
|---|---|---|
| `WorkloadTarget` | `samples/applications/OrderIntake/Domain/WorkloadTarget.cs` | one typed business intent for four source mappings |
| `TrialOrder` | `samples/applications/OrderIntake/Domain/TrialOrder.cs` | the actual bounded Entity payload |
| `OrderIntakeTrial` | `samples/applications/OrderIntake/Domain/OrderIntakeTrial.cs` | one owner for job state and the idempotent workload |
| `TrialReceipt` | `samples/applications/OrderIntake/Domain/TrialReceipt.cs` | immutable review evidence separated from Jobs mechanics |
| `TrialsController` / `TrialStatusView` | `samples/applications/OrderIntake/Controllers/` | custom HTTP and its projection remain controller-owned |
| routes, limits, corrections | `samples/applications/OrderIntake/Infrastructure/OrderIntakeConstants.cs` | stable application vocabulary has one owner |
| startup explanation | `samples/applications/OrderIntake/Initialization/OrderIntakeModule.cs` | one application composition/report owner without host ceremony |
| source mappings, requests, UI, optional services | sample root, `wwwroot/`, and `docker/compose.yml` | standard configuration and one dependency-free operator surface |
| cumulative proof | `tests/Suites/Samples/Koan.Samples.OrderIntake.Tests/` | real local worker/ledger/HTTP/facts/cleanup/correction contract |

**Coalescence:** closest patterns are DevPortal's named-source routing and GoldenJourney's Entity job; neither is
widened because provider routing is Data law and orchestration is Jobs law. OrderIntake owns only target vocabulary,
payload values, verification, and receipt. Source/provider plans compile at host composition; one execution performs
three bounded batch operations with no per-item discovery or provider negotiation. Disposition is `rebuild` for the
application and `delete` for benchmark services/interfaces, three technical Entity tiers, provider-pair migrations,
SignalR, direct SQLite configuration, synchronous execution, charts, and application-container scripts.

**Ergonomics:** humans read target → orders → verify → cleanup → receipt. IntelliSense stays on Entity/Jobs and one
business enum; agents see one route family and one status shape rather than modes, tiers, providers, and duplicate
engines. The only cognitive branch is choosing a target; Local always works and each optional branch carries its own
correction.

**Constraints satisfied:**

- all data work uses Entity statics; no repository/service layer;
- all HTTP uses attribute-routed controllers; the SignalR `MapHub` exception is deleted;
- stable routes, limits, source names, and correction commands are centralized; there is no tunable benchmark mode;
- the workload is explicitly bounded, so no unsupported stream or unpaged scan is used;
- no placeholder, compatibility path, or application-owned adapter tuning remains;
- public docs, sample index, solution membership, lockfile, and live R10 records update with the implementation.

**Risks:** remote connectors differ in startup/connect timeout behavior, so focused correction proof uses one
deliberately unreachable Documents source while configuration and source facts cover all three. A target source scope
must be disposed before every `ctx.Progress` and before handler return so the durable control plane never follows the
workload data plane.

## Focused acceptance

1. The renamed application uses the four-line host, direct capability references only, zero direct database-driver
   code, and no application service/repository/hub/bootstrap workaround.
2. A clean local run needs no Docker. One API request submits a bounded `Local` trial; Jobs completes it and the
   status response exposes a verified durable receipt with exact counts, phase timings, capabilities, and runtime.
3. The workload deletes only its own IDs. Repeating it produces a separate durable receipt and does not use
   `RemoveAll`, cross-provider migration, warmed identifiers, or unbounded work.
4. `Documents`, `Relational`, and `KeyValue` are typed named-source intents. Missing unselected infrastructure does
   not break initial readiness; selecting it yields both a corrective failed-job response and an honest unhealthy
   readiness report. Pinned compose services enable the same workflow.
5. UI, README, API, startup facts, and lockfile say “workload receipt,” never “objective winner,” “fastest,” or
   “architecture recommendation.” Environment and methodology limits are explicit.
6. Strict sample build and one focused cumulative test pass with warnings as errors. The test proves dashboard,
   submission, durable completion/progress, verified receipt, source capabilities, cleanup, readiness/facts, and
   clean host shutdown without release certification.
7. The solution, canonical sample index, active Jobs references, stale decision status, docs lint, diff check, and
   privacy scan agree with the new identity and claim boundary.

## Acceptance evidence

- The inherited benchmark stack, provider rankings, direct SQLite tuning, duplicate execution engines, SignalR,
  charts, start scripts, and application-container scaffold are deleted. The replacement is the standard four-line
  host, two business Entities, one typed target enum, one controller, one startup report, and one dependency-free UI.
- Data Core now distinguishes connector availability from runtime participation. The default winner and actually
  selected sources gate readiness; configured-but-unused optional routes remain visible and non-critical. MongoDB,
  PostgreSQL, Redis, JSON, and SQLite consume that shared law, while Redis named sources now honor their configured
  endpoint through one pooled connection per endpoint.
- `DataAdapterParticipationSpec` passes 3/3; JSON health passes 5/5; SQLite health passes 7/7; Mongo source-pool
  deduplication passes 2/2. These are focused owner tests, not release certification.
- Strict Release build of `OrderIntake` passes with zero warnings and errors. Its cumulative contract passes 1/1 in
  about one second and proves the dashboard, initial readiness, inactive optional connectors, local submission,
  durable worker completion/progress, canonical SQLite receipt, exact cleanup, persisted trial, unavailable Mongo
  correction, post-selection unhealthy readiness, complete facts, and clean host shutdown.
- The README, request file, UI, startup note, source mappings, compose file, solution membership, and lockfile agree:
  Local needs no Docker; optional infrastructure activates only when selected; receipts describe one bounded run and
  never rank providers.

R10-07 passes. `OrderIntake` is graduated public curriculum.
