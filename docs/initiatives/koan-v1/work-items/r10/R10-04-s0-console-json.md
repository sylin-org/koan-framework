---
type: SPEC
domain: framework
title: "R10-04 - Graduate S0.ConsoleJsonRepo"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: strict source, standard-host ownership, executable JSON/business/report/shutdown, and template proof
---

# R10-04 — Graduate S0.ConsoleJsonRepo

- Tranche: `T7B — maintained-sample graduation`
- Status: `passed`
- Depends on: R10-03 S1.Web graduation
- Owner: minimal console host, Entity statics, JSON persistence, truthful provider bounds, and process proof

## Task

Rebuild S0 into the smallest honest console application that proves Koan's standard host, Entity language,
reference-selected JSON persistence, query negotiation, startup explanation, and clean shutdown.

## Application intent

“Create today's local checklist, complete one task, reload the remaining open work from durable JSON, and print the
meaningful result.”

## Public expression

The complete application-facing surface is:

```csharp
using var app = new ServiceCollection().StartKoan();

await new Todo { Title = "Buy milk" }.Save();
var open = await Todo.Query(todo => !todo.Done);
```

- Reference: the `Sylin.Koan` foundation bundle, represented by its source project in this checkout. It supplies
  Core, Data, local Communication, and the JSON floor; no duplicate Data.Core/Abstractions references are authored.
- Model: one `Todo : Entity<Todo>` with `Title`, `Done`, and one business-named `Complete()` method.
- Configuration: none on the common path. JSON uses its documented `./data` default; standard Koan configuration
  can override `Koan:Data:Json:DirectoryPath` for tests or applications.
- Context: run from the sample directory so the content root, checked-in lockfile, and relative data directory agree.
- Runtime prerequisite: a writable local filesystem and .NET 10 SDK.

## Guarantee and correction

The reference guarantees that ordinary Entity saves and bounded materialized queries use the local JSON provider,
and that the standard Generic Host lifecycle starts and stops every referenced local capability. Startup must report
JSON selection and a matching composition lock without collection failure.

JSON does not advertise provider-bounded paging, so `AllStream()` and `QueryStream()` are not promised. The existing
corrective `QueryStreamRejectedException` names `missing-provider-bounded-paging` and tells the caller to choose a
capable adapter or materialize explicitly. S0 must teach `Query()`, not knowingly execute an unsupported stream.

No additional user actions exist beyond the reference, model, `StartKoan()`, Entity calls, and writable filesystem.

## Public concepts

| Concept | Why it is earned |
|---|---|
| `StartKoan()` | one-line synchronous console bootstrap over the standard .NET host lifecycle |
| `Entity<Todo>` | first-class persistence and query language |
| JSON reference | explicit local durable-provider intent |
| `Save`, `Query`, `Complete` | persistence, retrieval, and one business operation |
| `using var app` | standard ownership and graceful shutdown, enabled by returning `IHost` |

Batching and streaming are outside S0's concept budget. S1 owns cardinality teaching; larger samples own jobs,
transport, HTTP, and external infrastructure.

## Exploration evidence

### Docs read

- `docs/architecture/principles.md` establishes Entity-first, Reference = Intent, progressive complexity, and
  fail-loud provider bounds; all apply directly.
- `GOLDEN-SAMPLE-GRADUATION.md` requires one business result, strict build, truthful facts, and claimed-path proof.
- `docs/guides/composition-lockfile.md` establishes automatic build emission and content-root runtime comparison;
  its source-checkout note now understates the shared samples policy and must be corrected.
- `ARCH-0119` establishes `StartKoan()` as a one-line facade over one standard Generic Host and says explicit
  ownership is preferred; its public return type still makes ordinary `using var` impossible.
- `templates/koan-console` is the nearest public consumer; it uses `StartKoan()` but currently leaks the returned
  host while claiming graceful shutdown.

### Code read

- `samples/S0.ConsoleJsonRepo/Program.cs` performs CRUD/batch work, owns an argument-based adapter override, and
  then always calls unsupported `Todo.AllStream()`.
- `Todo.cs` is a plain unsealed Entity with only `Title`; it has no business state or operation.
- `S0.ConsoleJsonRepo.csproj` repeats JSON, Data.Abstractions, Data.Core, DI, and source-build concerns instead of
  expressing the one foundation intent.
- `ServiceCollectionExtensions.StartKoan()` already builds, starts, owns, and disposes a standard `IHost`, but
  returns `IServiceProvider`; its private wrapper already implements disposal.
- `JsonRepository` declares scan-based materialized filter execution and no provider-bounded paging;
  `QueryStreamCoordinator` correctly rejects unbounded streams with a corrective error.

### Existing constants, options, and shared types

- Already exists: `JsonDataOptions` and canonical `Koan:Data:Json:DirectoryPath` configuration.
- Already exists: `RemoveStrategy.Safe`, Entity collection `Save`, `Query`, provider facts, lockfile facts, and
  standard `Microsoft.Extensions.Hosting.IHost` ownership.
- Already exists: process execution/output APIs in .NET; no sample harness is needed.
- Needs to be created: no application constants, options, DTOs, provider selector, or module.

## Coalescence

- Closest pattern: `templates/koan-console/Program.cs` plus `ARCH-0119`.
- Decision owner: Data.Core owns the console convenience; .NET Generic Host owns lifecycle; JSON owns filesystem
  mechanics and capabilities; S0 owns only checklist behavior and presentation.
- State lifetime/hot path: the host and ambient lease live for one process; provider and query plans remain
  framework-memoized; no new per-operation discovery is introduced.
- Repeated mechanic: S0 casts the result to `IDisposable`, while the template does not dispose it at all.
- Specificity and disposition: rebuild the Data.Core facade return as standard `IHost`, keep the one-line method,
  then update its two public consumers and focused owner tests. A Koan-specific host interface would add a concept;
  application-local casts preserve the leak/cognitive branch.
- Delete: argument-owned JSON options, redundant project/package references, unsupported stream path, batch lesson,
  startup comment scaffold, and `start.bat`.
- Ergonomics: `using var app = services.StartKoan()` reads as ordinary .NET ownership, appears naturally in
  IntelliSense, and gives agents one obvious lifecycle. Entity calls remain direct business verbs.

## Code placement

| Change | Location | Why |
|---|---|---|
| Return standard `IHost` and make the owner wrapper implement it | `src/Koan.Data.Core/ServiceCollectionExtensions.cs` | Data.Core owns the existing console facade; standard .NET owns the returned lifecycle contract |
| Focused ownership assertions | existing Data.Core and Communication lifecycle specs | prove the owner and consumer boundary without a broad certification run |
| Checklist state and `Complete()` | `samples/S0.ConsoleJsonRepo/Todo.cs` | the operation belongs to the Entity business model |
| Deterministic console journey | `samples/S0.ConsoleJsonRepo/Program.cs` | application presentation and orchestration only |
| Minimal reference intent | `samples/S0.ConsoleJsonRepo/S0.ConsoleJsonRepo.csproj` | project references state the one foundation capability choice |
| Process-level golden contract | `tests/Suites/Samples/Koan.Samples.S0Console.Tests/` | execute the real console, startup report, JSON file, and shutdown |
| Public contract | sample README plus requests/index/initiative docs where applicable | keep source, shortest path, maturity, and limitations aligned |

## Constraints

- Entity statics remain the only data access; no repository/service is introduced.
- No HTTP surface exists, so no inline endpoint issue exists.
- No placeholder, app constants/options/DTOs, or commented scaffold is created.
- Materialized `Query()` is used because JSON correctly rejects streams.
- The standard host and connector capability declaration remain the single lifecycle/query owners.
- Documentation and focused tests change with the public return contract.

## Focused acceptance

1. `Set-Location samples/S0.ConsoleJsonRepo; dotnet run` exits 0 after printing the deterministic checklist result.
2. Boot reports JSON selected, lockfile matched, and no collection failure; shutdown is clean.
3. An isolated process test verifies the JSON file and business output using only standard configuration override.
4. Data.Core/Communication lifecycle tests pass after the `IHost` return refinement.
5. Strict Data.Core, S0, and sample-test builds pass with zero warnings/errors.
6. README, template, composition guide, sample index, solution membership, diff, docs, and privacy checks agree.

## Acceptance evidence

- `StartKoan()` now returns the standard `IHost` it already owns. The private owner exposes `Services`, retains the
  ambient-provider role, and releases the lease before host stop/disposal. No Koan host abstraction was introduced.
- Data.Core's five ownership cases and Communication's three console-lifecycle cases pass (5/5 and 3/3).
- S0 now has one direct foundation reference, one sealed Entity with one business method, and one deterministic
  materialized checklist. The three duplicate Koan references, direct DI package, app-owned JSON option override,
  batch lesson, unsupported stream, scaffold comment, and launch helper are gone.
- `dotnet build src/Koan.Data.Core/Koan.Data.Core.csproj -c Release -p:TreatWarningsAsErrors=true` and the equivalent
  strict S0 build both pass with 0 warnings and 0 errors.
- Running `dotnet run` from the sample directory exits 0 after reporting the JSON election, four-module local ring,
  `8 modules · lockfile ok`, 3 total / 1 complete / 2 open, and orderly hosted-service shutdown.
- `Koan.Samples.S0Console.Tests` passes 1/1. It runs the real Release process with only the standard JSON directory
  configuration override, verifies the business output, matching lock, no collection failure/unhandled exception,
  JSON persistence including the completed item, and clean exit.
- The public console template now uses ordinary `using var` host ownership. Its focused packaging/compiler suite
  passes 13/13.
- The source composition guide records the shared executable-sample policy; S0's lockfile has one direct
  `Sylin.Koan` reference and the correct eight-module closure.
- Docs lint passes with 0 errors / 1,624 historical warnings; changed instructional docs contain no marked compile
  blocks; `git diff --check` and the scoped private-identity scan are clean.
- Focused broad test projects still print one unrelated Qdrant and two unrelated Tenancy XML-documentation warnings.
  Their tests pass; the changed production owners are warning-clean. No release-certification suite was run.
