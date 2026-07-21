---
type: SPEC
domain: framework
title: "R10-06 - Graduate g1c2.GardenCoopEmbedded"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: strict source, AI/vector owners, cumulative TestServer, live source/correction, and win-x64 self-contained deployment
---

# R10-06 — Graduate g1c2.GardenCoopEmbedded

- Tranche: `T7B — active-sample graduation`
- Status: `passed`
- Depends on: R10-05 S10.DevPortal graduation
- Owner: local-first AI composition, canonical module startup, Entity embeddings, and truthful deployment evidence

## Task

Break and rebuild g1c2 into the golden proof that a useful semantic application can run entirely on local,
in-process providers. Remove manual host ownership, false single-executable and messaging claims, incidental
provider naming, and any sample-local compensation for framework startup order.

## Application intent

“Save five local co-op produce listings and find ‘ripe red tomato’ semantically, using only in-process SQLite,
sqlite-vec, and ONNX.”

## Public expression

The smallest honest host remains the canonical four lines:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

The complete expression is one `Produce : Entity<Produce>` with `[Embedding]`, one generated Entity controller,
one scored semantic-search controller, and one application `KoanModule` whose only earned responsibility is
deterministic starter data plus its startup explanation. References select SQLite, sqlite-vec, and ONNX; standard
configuration supplies the shared database and bundled model paths. No container or external service is required.

## Guarantee and correction

The supported source path guarantees five durable local listings, vectors produced by the bundled ONNX model,
and a semantic search where “ripe red tomato” ranks Heirloom Tomatoes first. Rows and vectors share one SQLite
file; all AI and vector work stays in-process.

The deployment claim is a self-contained folder, not one executable: native ONNX/SQLite libraries, model assets,
and settings remain beside the app. NativeAOT and cross-RID claims are unsupported until independently built and
run. A configured missing model must fail loud with a corrective path; it must not silently disable embeddings.

## Exploration evidence

### Docs and contracts read

- The product constitution and R10 graduation standard require one meaningful cumulative result, truthful claims,
  canonical hosting, inspectable startup/facts, and repair at the real framework owner.
- ARCH-0116 makes `KoanModule.Register/Start/Report` the one boot-time lifecycle and explicitly rejects internal
  initializer lifecycles as a second owner.
- ARCH-0115 keeps contributor contracts at the concern boundary and compiles them at the runtime owner.
- The Entity semantics contract and Data.AI README establish `[Embedding]` plus `Save()` as the canonical automatic
  embed-and-index path; query embedding remains an explicit AI operation.

### Code and baseline read

- `Program.cs` manually assigns `AppHost.Current`, calls `StartAsync`, seeds, then waits for shutdown solely because
  `AiAdapterContributorInitializer` is a later `IHostedService` instead of part of `AiModule.Start`.
- `KoanModuleHost` runs every active module's `Start` before later hosted services. All module registrations are
  already complete, so AI can compile all referenced `IAiAdapterContributor` instances inside its own `Start`.
- ONNX contributes both an adapter and an AI source when a configured model exists; missing model/vocabulary errors
  are currently logged by the contributor coordinator and surface later when embedding is attempted.
- `Produce.Save()` already invokes the Data.AI lifecycle and sqlite-vec indexing; the custom controller adds scored
  query embedding and vector lookup without a service/repository layer.
- `VectorService` owns a host-singleton repository cache, but its decorated sqlite-vec repository was never disposed;
  a passing business test therefore failed teardown because the database connection survived host disposal.
- Strict Release source build passes with zero warnings/errors. A live source run returns five rows and ranks
  Heirloom Tomatoes first for the target query; actual default HTTP is port 5000, not the README's 5099.
- A win-x64 self-contained single-file publish emits a 110 MB executable plus native ONNX/SQLite libraries, model
  assets, configuration, symbols, and XML documentation. The README's “one `.exe`” framing is false.
- The project references no messaging package, despite README and app-description claims that an in-memory bus is
  part of this slice.

### Existing concepts and coalescence

- Keep: `KoanModule`, `[After]`, `IAiAdapterContributor`, AI adapter/source registries, `[Embedding]`, Entity Save,
  Vector Search, EntityController, standard options/configuration, runtime facts, and the build-generated lockfile.
- Create only: an application module, application API route constants if needed, compact response records, focused
  executable sample proof, and a dependency-free result page/request file.
- Do not create: another initializer interface, public bootstrap hook, repository/service abstraction, provider
  selector, hosted seeder, application host binder, or sample-specific AI readiness loop.
- Coalescence owner: `AiModule.Start` compiles AI contributors once after all registrations; connector modules only
  contribute; the application module only seeds business data after AI. This removes a hosted-service lifecycle and
  lets every consumer use normal `AddKoan()` plus `RunAsync()`.
- Resource owner: `VectorService` disposes the repository cache it creates, while `ScopedVectorRepository` owns its
  inner provider lifetime. Concurrent first resolution keeps one winner and disposes the losing repository.
- Hot path: election and model/session construction remain host-singleton startup work; Entity operations and query
  embedding reuse the compiled registries and ONNX session. No per-request discovery or reflection is added.
- Superseded paths: manual `AppHost` assignment/start/wait, free-floating static seed bootstrap, explicit vector
  adapter decoration if reference election proves sufficient, false messaging/single-exe/cross-RID claims, and
  aspirational NativeAOT material not backed by executable proof.

## Code placement

| Change | Location | Reason |
|---|---|---|
| contributor compilation | `src/Koan.AI/Initialization/` and `AiModule.Start` | AI is the lifecycle and registry owner |
| focused lifecycle proof | `tests/Suites/AI/Unit/Koan.Tests.AI.Unit/` | proves contributors are compiled through the module, not a hosted service |
| starter data and report | `samples/guides/g1c2.GardenCoopEmbedded/Initialization/` | earned application composition-time responsibility |
| host/entity/search/UI/config | g1c2 sample root | smallest business-readable application surface |
| cumulative sample contract | `tests/Suites/Samples/` | real host, seed, search, facts/readiness, correction, and shutdown |
| public truth | README, sample index, R10/NOW/PROGRESS | source, claims, evidence, and portfolio state remain aligned |

## Focused acceptance

1. The application uses only `AddKoan()`, `Build()`, and awaited `RunAsync()` for host ownership; no manual binding,
   startup, seeding call, or hosted-service workaround remains.
2. `AiModule.Start` compiles every referenced contributor after registrations and before an explicitly ordered
   application module seeds embedded Entities; a focused test guards this lifecycle ownership.
3. A clean source run needs no Docker, creates five records/vectors, returns Heirloom Tomatoes first for the target
   query, keeps readiness healthy, and reports complete facts without collection failure.
4. The application-facing code names business intent; provider mechanics remain in references/configuration and
   startup facts. Explicit vector routing remains only if default election evidence requires it.
5. A missing configured model produces a loud, safe, actionable startup failure rather than a healthy partial app.
6. Strict changed-owner/sample/test builds pass. A real cumulative sample test proves the meaningful path and clean
   host lifecycle without invoking release certification.
7. The self-contained publish is run from its folder and documented as the exact artifact shape observed; unproved
   NativeAOT, RID, one-file, and messaging claims are removed.
8. README, UI, requests, lockfile, solution membership, sample index, docs, diff, and privacy checks agree.

## Acceptance evidence

- g1c2 is now a standard Web SDK application with five direct capability references, a four-line host, one
  `Produce` Entity, one generated controller, one scored search controller, and one application module. Manual
  `AppHost` assignment, host start/wait, free-floating seed call, explicit sqlite-vec decoration, unsupported
  messaging, NativeAOT roots, and aspirational bootstrap comments are gone.
- `AiModule.Start` now compiles every registered `IAiAdapterContributor` and immediately projects the resulting
  adapter/source roster. The old initializer and one-shot provenance hosted-service lifecycles are coalesced into
  the retained module. Unhandled configured-intent failures propagate through host startup instead of becoming a
  healthy partial application.
- The new focused lifecycle proof passes 2/2; the complete warning-as-error AI unit suite passes 160/160. A real
  missing-model run exits 1 during startup and names the missing resolved path plus the exact
  `Koan:Ai:Onnx:ModelPath` correction.
- A fresh source run, without Docker or provider annotations, serves the dependency-free dashboard, contains five
  listings, ranks `heirloom-tomatoes` first for `ripe red tomato` at approximately 0.731, returns readiness 200,
  reports complete facts, records SQLite direct-reference election and a matched 20-module lockfile, and contains
  no collection failure.
- `Koan.Samples.GardenCoopEmbedded.Tests` is solution-owned and passes 1/1 with warnings as errors. It proves the
  real module order, ONNX seeding, Entity API, scored semantic result, dashboard, readiness, composition facts,
  clean host shutdown, and immediate deletion of its isolated database.
- That teardown proof initially exposed a host-lifetime defect: `VectorService` retained sqlite-vec's open
  connection. The service now disposes its memoized repositories, the segmentation decorator forwards lifetime,
  and concurrent first resolution disposes the losing instance. The unchanged cumulative test turns green and the
  sqlite-vec owner suite passes 4/4.
- Strict sample build passes with 0 warnings/errors. The retained win-x64 command initially failed its strict
  single-file analysis because verbose Core bootstrap diagnostics read `Assembly.Location`; the path was unused and
  removed. Its focused bootstrap gate passes 2/2 and strict publish now succeeds.
- The published executable was run from its output folder. It returns the same first semantic result and readiness
  200 without installed .NET or external services. Exact observed shape is 56 entries: a roughly 110 MB executable,
  bundled-model directory, static UI, settings/lockfile, native ONNX/SQLite libraries, and build symbols/docs. The
  README calls this a self-contained folder and explicitly disclaims literal one-file, NativeAOT, and untested RID
  portability.
- `README`, dashboard, `requests.http`, launch profile, app identity, composition lock, solution membership, and
  public sample index now tell the same local semantic-search story. Mixed AI adapter lifetime ownership remains an
  explicit post-cycle design item (PMC-030), not an ONNX/sample workaround.
- Docs lint passes with 0 errors / 1,626 historical warnings; the changed sample READMEs contain no opted-in
  instructional compile blocks; `git diff --check` is clean apart from line-ending notices; the scoped private-
  identity scan has no matches; no g1c2 proof process or port remains active.
- No release-certification suite, package publication, tag, push, remote mutation, or private dogfood identity was
  used.
