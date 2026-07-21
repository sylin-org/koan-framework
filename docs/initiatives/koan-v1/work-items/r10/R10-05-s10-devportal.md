---
type: SPEC
domain: framework
title: "R10-05 - Graduate S10.DevPortal"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: strict source plus local SQLite, missing-channel correction, real Mongo/Postgres, HTTP, readiness, facts, and idempotency proof
---

# R10-05 — Graduate S10.DevPortal

- Tranche: `T7B — active-sample graduation`
- Status: `passed`
- Depends on: R10-04 S0.ConsoleJsonRepo graduation
- Owner: business-aligned provider composition, named data sources, Entity transfer, and truthful optional infrastructure

## Task

Break and rebuild S10 into a golden application that demonstrates one meaningful multi-provider workflow rather
than a framework feature showroom. Preserve the central promise—one Entity grammar across providers—while deleting
false switching, fallback, set-routing, benchmark, and deployment claims.

## Application intent

“Draft developer articles locally, approve the ones ready for readers, and publish that approved set to a named
preview or production channel without changing the Article model or persistence grammar.”

## Public expression

The smallest honest application expression is:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();

public sealed class Article : Entity<Article>
{
    public static Task<TransferResult<string>> PublishApproved(
        PublicationChannel channel,
        CancellationToken ct = default)
        => Article.Copy(article => article.Status == ArticleStatus.Approved)
            .To(source: channel.ToString())
            .Run(ct);
}
```

Complete user action surface:

- references: Web.Extensions plus SQLite, Mongo, and Postgres connectors;
- code: one `Article` Entity, its business status/approval/publication verbs, one generated controller, and one
  publication controller;
- configuration: `Default` and `Preview` named sources use local SQLite; optional `Documents` and `Relational`
  sources name Mongo and Postgres endpoints;
- context: transfer routing is expressed through typed business channel names and compiled by Data's existing
  source/adapter pipeline;
- runtime: .NET 10 and a writable local directory for the supported shortest path; Docker is required only when
  explicitly exercising the optional Mongo or Postgres channels.

No manual framework registration, provider service, repository, runtime-global switch, or application module is
required. The module would own no genuine composition-time business policy.

## Guarantee and correction

The default run guarantees a zero-infrastructure editorial store and preview channel backed by separate SQLite
files. Publishing copies only approved articles, preserves Entity identities, returns a fixed transfer receipt, and
is idempotent because destination writes are upserts. The default editorial collection remains unchanged.

Selecting `Documents` or `Relational` guarantees only that the named source resolves to the referenced connector
and that its declared capabilities govern the same Entity/transfer expression. If its endpoint is absent or a
required query/write capability cannot be honored, the operation must fail loudly with provider/source context and
a safe correction; S10 must not report a switch, fallback, or successful publication.

The current transfer builder materializes the selected source before destination batches. S10 therefore keeps the
approved demo set deliberately small and makes no large-data, checkpoint, or streaming guarantee.

## Public concepts

| Concept | Business or guarantee reason |
|---|---|
| `Article : Entity<Article>` | one domain and persistence grammar across every channel |
| `ArticleStatus` and `Approve()` | publication eligibility is business state, not controller branching |
| `PublicationChannel` | typed business vocabulary prevents arbitrary provider strings at the HTTP boundary |
| named Data sources | configuration maps stable business channels to replaceable provider mechanics |
| `Article.Copy(...).To(source).Run()` | existing Entity-first operation expresses idempotent publication and returns evidence |
| `EntityController<Article>` | generated CRUD remains the canonical article API |
| publication controller | custom HTTP owns reset/publish/read orchestration and corrective responses |

Technologies, users, comments, random seed services, performance timing, set-routing claims, runtime-global
switching, and provider-specific branching do not earn a place in this sample.

## Exploration evidence

### Docs read

- `docs/architecture/product-constitution.md` makes meaningful business steps, one canonical intent, provider
  honesty, and executable samples durable product law; it now records the all-active-samples mandate.
- `docs/architecture/principles.md` establishes Entity-first data, Reference = Intent, named context escape hatches,
  deterministic configuration, capability negotiation, and fail-loud behavior.
- `R10-golden-samples.md` and `GOLDEN-SAMPLE-GRADUATION.md` require strict source, one meaningful result, truthful
  startup/facts, economical executable proof, and no sample-local framework repair.
- `DATA-0079` establishes the Entity `Copy`/`Move`/`Mirror` grammar and Source XOR Adapter contract; it is the closest
  existing expression but its original streaming language is superseded by current implementation truth.
- `docs/guides/entity-capabilities-howto.md` correctly states that transfer source selection is currently
  materialized and that audit batches are not checkpoints.

### Code read

- `S10.DevPortal.csproj` directly references low-level Core/Web/Data projects plus three connectors; the direct
  connector set elects Mongo by priority unless the application names a default source.
- `Program.cs` adds an unexplained proxy feature, registers a sample service, creates a data directory, calls
  `RunAsync()` without awaiting it, and therefore exits immediately after boot.
- `DemoController` labels an ambient scope as a global provider switch, reports a TODO provider identity, catches
  every error into ad-hoc responses, benchmarks incomparable endpoints, and advertises capabilities it does not prove.
- `DemoSeedService` creates random framework-demo content behind a service/interface pair and mixes generation,
  parent persistence, and business graph creation without a stable result.
- the four Entities and five controllers are mostly commentary about framework features; relationship endpoints
  repeatedly load entire collections, interpolate string queries, and do not support the central provider claim.
- `DataProviderCatalog` and `DataDefaultProviderPlan` already own deterministic provider election and exact default
  source overrides; application-local election code would create a second owner.
- `EntityTransferBuilderBase` and `CopyTransferBuilder` already own Source XOR Adapter routing, predicate selection,
  destination batching, idempotent upsert, audit, warnings, and a result receipt; the source is currently materialized.

### Baseline

- normal Release build succeeds with two warnings: one S10 unawaited `RunAsync()` and one Mongo XML `cref` defect;
- strict Release build correctly fails on the Mongo warning before reaching the application warning;
- the documented local `dotnet run` starts, reports `data:default = mongo`, then exits successfully in roughly four
  seconds because the host task is abandoned; no meaningful UI or business action remains available;
- Docker currently has no S10 services running, which matches the documented local path's missing prerequisite and
  exposes that “local development” is not a supported meaningful path today;
- README claims .NET 9 for a net10.0 project and describes APIs, fallback, set routing, performance, ports, container
  behavior, and provider switching that current code does not truthfully establish.

### Existing constants, options, and shared types

- Already exists: `DataSourceRegistry.SourceDefinition`, standard `Koan:Data:Sources:{name}` configuration,
  `EntityContext.Source`, `Data<T,K>.Capabilities`, Entity transfer builders/results/audit, and provider receipts/facts.
- Already exists: standard enum model binding and ASP.NET controller routing for a typed channel boundary.
- Needs to be created: application-owned `ArticleStatus`, `PublicationChannel`, deterministic Article seed/reset,
  compact publication response records, and a local dashboard contract.
- Does not need to be created: provider selector, provider registry, custom options, repository/service abstraction,
  application module, runtime switch, benchmark engine, or framework transfer wrapper.

## Coalescence

- Closest pattern: Entity `Copy` to a named source in `DATA-0079` and the current transfer implementation.
- Decision owner: Core owns generic provider catalog mechanics; Data owns source/provider resolution, capabilities,
  transfer execution, and receipts; connectors own endpoints/mechanics; S10 owns editorial status and channel intent.
- State lifetime/hot path: provider/source plans remain host-owned and memoized; each bounded publication builds one
  transfer operation and executes existing repositories. No reflection, discovery, or election loop is added.
- Repeated mechanics: S10 currently reimplements provider selection, capability narration, data movement, timing,
  and error shaping in controllers while its README invents automatic fallback.
- Specificity/disposition: keep Data's shared catalog and Entity transfer grammar; rebuild the application around
  named sources; delete the duplicated demo/service/provider machinery. Do not widen Core or create a generic sample
  platform—the existing responsibility chokepoints are sufficient.
- Human/agent ergonomics: `Article.PublishApproved(PublicationChannel.Preview)` reads as business intent;
  IntelliSense exposes a finite channel vocabulary; the underlying `.Copy().To(source).Run()` remains inspectable;
  configuration and startup facts explain the provider mapping without provider strings leaking through controllers.
- Superseded paths to delete: `IDemoSeedService`, `DemoSeedService`, Demo controller, three incidental Entities and
  their controllers, AngularJS views/controllers, app-owned data-directory creation, proxy opt-in, unawaited host,
  Docker application image, launch helpers, stale docs, and false endpoints/claims.

## Code placement

| Change | Location | Justification |
|---|---|---|
| article state and business verbs | `samples/S10.DevPortal/Models/Article.cs` | Entity owns approval and the article-centered publication expression |
| typed publication channels | `samples/S10.DevPortal/Models/PublicationChannel.cs` | application business vocabulary; standard enum binding avoids a new framework concept |
| generated article API | `samples/S10.DevPortal/Controllers/ArticlesController.cs` | canonical Entity HTTP declaration |
| bounded reset/publish/read endpoints and response records | `samples/S10.DevPortal/Controllers/PublicationController.cs` | controller owns custom HTTP orchestration; records stay request/response-local |
| four-line host and app identity | `samples/S10.DevPortal/Program.cs` | no application module is earned |
| named source mappings | `samples/S10.DevPortal/appsettings.json` | standard Data configuration owns channel-to-provider mechanics |
| optional provider services only | `samples/S10.DevPortal/docker/compose.yml` | Docker is an explicit external prerequisite, not the shortest path |
| dependency-free dashboard and request examples | `samples/S10.DevPortal/wwwroot/`, `requests.http` | one visible cumulative business story without frontend scaffolding |
| cumulative local/external proof | `tests/Suites/Samples/Koan.Samples.S10DevPortal.Tests/` | real host, SQLite publication, idempotency, facts/readiness, and optional connector boundaries |
| Mongo XML reference repair | `src/Connectors/Data/Mongo/MongoConnectionString.cs` | the connector owns its documentation warning; S10 must not suppress it |
| public truth and portfolio state | sample README, sample index, R10/NOW/PROGRESS | source, claims, maturity, and evidence remain one contract |

## Constraints satisfied

- Entity statics and Entity transfer are the only data access; no repository/service layer is introduced.
- All HTTP remains controller-owned; no `MapGet`/`MapPost` endpoints are planned.
- Stable channel identity is a typed enum; tunable provider settings remain standard source configuration.
- No placeholder classes, commented scaffolds, or manual host binding survive.
- The bounded sample deliberately uses current materialized transfer semantics; no false streaming claim remains.
- Documentation, startup/facts, source, dashboard, requests, and focused tests change together.

## Risks

- The current transfer predicate may require provider LINQ support; external-source verification must prove the
  selected source path or preserve a clear bounded fallback warning without claiming silent universal parity.
- Inactive referenced remote connectors must remain non-gating until their named source participates. If readiness
  disagrees, repair adapter participation/health at the shared owner rather than hiding it in S10.
- Docker-backed Mongo/Postgres evidence should run once at the sample boundary, not on every inner edit cycle.

## Focused acceptance

1. `dotnet run --project samples/S10.DevPortal` remains alive and reaches a meaningful local reset → approve →
   publish-to-preview → read-preview result with no Docker or manual data directory.
2. Repeating publication produces the same approved destination identities without duplicating records; drafts and
   the editorial source remain unchanged.
3. Startup/facts show the configured SQLite default, three connector candidates, named-source decisions/usage, a
   matched lockfile, healthy readiness, and no collection failure while remote channels are unused.
4. Missing optional infrastructure fails the selected channel loudly and does not degrade or mutate the default.
5. Focused Docker-backed proof publishes the same approved set to Mongo and Postgres, or the corresponding public
   channel claim is removed/qualified before graduation.
6. Strict changed-owner/sample/test builds pass with zero warnings/errors; one cumulative sample test proves the
   business path and real host lifecycle.
7. README, dashboard, requests, compose, solution/test membership, sample index, docs, diff, and privacy checks agree.

## Acceptance evidence

- S10 is now one sealed `Article` Entity, two business enums, one generated controller, one publication controller,
  and the standard four-line host. The host awaits `RunAsync()` and stays alive; no manual binding, directory
  ceremony, app module, service/repository, or provider selector remains.
- Direct references fell to Web.Extensions and the three intentional connectors. `Default`, `Preview`, `Documents`,
  and `Relational` are standard named sources; explicit default configuration elects SQLite rather than silently
  accepting Mongo's higher direct-reference priority.
- The real local application returns dashboard/readiness 200, resets to 3 total / 2 approved / 1 draft, publishes
  the same two IDs to Preview twice, retains two destination rows, preserves all three editorial rows, reports
  complete facts, and contains zero collection failures.
- `Koan.Samples.S10DevPortal.Tests` passes 1/1 with warnings as errors. Its real TestServer lifecycle proves the
  dashboard, healthy unused remote connectors, deterministic reset, idempotent separate-file SQLite publication,
  unavailable-Mongo 503 correction, unchanged default store, configured source facts, complete facts, and clean stop.
- Focused Docker Compose evidence used healthy `mongo:7.0` and `postgres:16` services only. Both Documents and
  Relational publications returned 200, read/copied exactly two records, preserved the same two IDs on repetition,
  exposed their negotiated capability sets, retained readiness 200 and complete facts, and produced no collection
  failure. The application and both services were stopped afterward.
- `dotnet build samples/S10.DevPortal/S10.DevPortal.csproj -c Release -p:TreatWarningsAsErrors=true` passes with
  0 warnings / 0 errors. The Mongo XML `cref` defect uncovered by the baseline was repaired at the connector owner.
- The random seed service/interface, Demo controller, three incidental Entities and four controllers, AngularJS/CDN
  UI, proxy opt-in, runtime-switch/performance/set-routing/bulk scaffolds, application Dockerfile, Redis service,
  launch helper, permissive development setting, and false/stale README claims are deleted.
- The replacement dashboard and requests file tell one cumulative editorial/publication story. README explicitly
  bounds current transfer materialization and disclaims fallback, global switching, benchmarks, checkpoints,
  streaming, and cross-provider transactions.
- The composition lock now records exactly four direct capability references and the expected 14-module closure.
  The sample and its one-test contract are both solution-owned; the canonical sample index promotes S10.
- Docs lint passes with 0 errors / 1,626 historical warnings; changed-example validation has no instructional files
  in scope; `git diff --check` is clean apart from line-ending notices; the scoped private-identity scan has no hits.
  No release-certification suite was run.
