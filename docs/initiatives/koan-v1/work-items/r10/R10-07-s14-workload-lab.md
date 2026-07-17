---
type: SPEC
domain: framework
title: "R10-07 - Rebuild S14 as the Provider Workload Lab"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: in-progress
  scope: current-behavior, public-claim, workload, provider, Jobs, UI, and deployment discovery complete
---

# R10-07 — Rebuild S14 as the provider workload lab

- Tranche: `T7B — active-sample graduation`
- Status: `in-progress`
- Depends on: R10-06 g1c2.GardenCoopEmbedded graduation
- Owner: honest named-source trials, durable Entity-first work, comparable receipts, and corrective optional infrastructure

## Task

Break and rebuild `S14.AdapterBench` as `S14.WorkloadLab`. Retire the premise that one synthetic dashboard can
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
the job must fail loudly and its status surface must give the exact compose command/correction. Starting the pinned
sample services makes the same workload available without application code changes.

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
| host, configuration, requests, UI | `samples/S14.WorkloadLab/` | one obvious executable application |
| order and trial semantics | `samples/S14.WorkloadLab/Domain/` | code reads as the business workload and receipt |
| submit/status/target projection | `samples/S14.WorkloadLab/Controllers/` | custom HTTP remains controller-owned |
| optional services | `samples/S14.WorkloadLab/docker/compose.yml` | infrastructure mechanics stay outside application code |
| cumulative contract | `tests/Suites/Samples/Koan.Samples.S14WorkloadLab.Tests/` | real local host, durable job, receipt, facts, cleanup, and correction |
| public truth | sample README/index, DX-0044, Jobs card, R10/NOW/PROGRESS | no stale benchmark recommendation remains active |

## Focused acceptance

1. The renamed application uses the four-line host, direct capability references only, zero direct database-driver
   code, and no application service/repository/hub/bootstrap workaround.
2. A clean local run needs no Docker. One API request submits a bounded `Local` trial; Jobs completes it and the
   status response exposes a verified durable receipt with exact counts, phase timings, capabilities, and runtime.
3. The workload deletes only its own IDs. Repeating it produces a separate durable receipt and does not use
   `RemoveAll`, cross-provider migration, warmed identifiers, or unbounded work.
4. `Documents`, `Relational`, and `KeyValue` are typed named-source intents. Missing infrastructure does not break
   local readiness and yields a corrective failed-job response; pinned compose services enable the same workflow.
5. UI, README, API, startup facts, and lockfile say “workload receipt,” never “objective winner,” “fastest,” or
   “architecture recommendation.” Environment and methodology limits are explicit.
6. Strict sample build and one focused cumulative test pass with warnings as errors. The test proves dashboard,
   submission, durable completion/progress, verified receipt, source capabilities, cleanup, readiness/facts, and
   clean host shutdown without release certification.
7. The solution, canonical sample index, active Jobs references, stale decision status, docs lint, diff check, and
   privacy scan agree with the new identity and claim boundary.

## Acceptance evidence

Pending implementation and focused execution.
