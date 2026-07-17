---
type: SPEC
domain: framework
title: "R10-01 - Graduate GardenCoop"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: strict source, lifecycle truth, business/HTTP/facts/dashboard path, and win-x64 NativeAOT runtime
---

# R10-01 — Graduate GardenCoop

- Tranche: `T7B — maintained-sample graduation`
- Status: `passed`
- Depends on: active R10 parent
- Unlocks: reusable golden-sample evidence template and next-sample inventory
- Owner: GardenCoop application semantics and proof; framework defects remain framework-owned

## Application intent

“Receive garden sensor readings, bind them to plots, and automatically create one watering reminder when
recent soil humidity is dry; acknowledge that reminder when the readings recover.”

## Smallest honest expression

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();
var app = builder.Build();
await app.RunAsync();
```

The complete application surface is:

- direct project/package references for Core/Data/SQLite, Web, OpenAPI, Admin, Auth Services, Auth, and the
  Development Test auth connector;
- `Entity<T>` models for members, plots, sensors, readings, and reminders;
- `EntityController<T>` declarations plus one controller-owned recent-readings query;
- Entity Lifecycle rules for sensor normalization/binding, reminder creation/recovery, and notification narration;
- one discovered `GardenCoopModule : KoanModule` that installs those business rules during composition, seeds
  starter business data during host startup, and reports the application behavior;
- no required configuration, external service, manual host binding, bootstrap callback, or provider selection.

## Guarantee and correction

Parameterless `AddKoan()` composes the referenced capabilities and application module exactly once. The standard
host binds Entity statics before the application module starts; seeding and lifecycle policy are ready before
traffic. SQLite supplies the zero-configuration durable floor. Startup/facts explain the elected modules and
provider without false collection failures.

Invalid composition or DI fails host construction/startup. Seed failure fails startup instead of serving a
half-initialized sample. Invalid sensor input or unsupported operations fail through their domain/framework
contracts. A NativeAOT or auth/admin incompatibility fails its focused gate and cannot remain a README claim.

## Exploration evidence

### Docs read

- `docs/engineering/index.md` — requires Entity-first access, controller-owned HTTP, strict project hygiene, and
  current companion evidence.
- `docs/architecture/principles.md` — establishes parameterless `AddKoan()`, Reference = Intent, one canonical
  expression, self-reporting infrastructure, and application modules only for real application policy.
- root `README.md` — establishes meaningful first result and boot facts as product behavior.
- `samples/README.md` and `samples/CATALOG.md` — establish curriculum intent but currently disagree about project
  inventory and maturity; R10 owns reconciliation.
- GardenCoop `README.md` — promises local operation, automatic reminders, Admin/Test auth, and NativeAOT.

### Code read

- `Program.cs` — still uses sample logging/lifecycle decoration, `AddKoan(GardenAutomation.Configure)`, manual
  `AppHost.Current`, and pre-host seeding; the already-intended deletion of legacy registry dumps is preserved.
- `GardenAutomation.cs` — owns coherent garden business policy and correctly uses Entity Lifecycle.
- `Reading`, `Sensor`, and `Reminder` — use Entity statics and business-named methods; no repository layer exists.
- controllers — use `EntityController<T>` and attribute routing; only recent readings adds a business route.
- `GardenSeeder` plus `GardenSeederRunner` — the seeder is meaningful, while the runner is a one-line indirection.
- SnapVault/Meridian application modules — closest evidence that one application module can own composition,
  startup work, and reporting without leaking host mechanics into `Program.cs`.

### Existing vocabulary

- **Already exists:** `AddKoan()`, `KoanModule.Register/Start/Report`, Entity Lifecycle, Entity statics,
  `EntityController<T>`, SQLite local defaults, application identity, facts, standard ASP.NET host/testing.
- **Needs creation:** GardenCoop's application module, stable Garden API route constants, and executable
  sample-specific conformance proof.
- **Does not need creation:** repository/service abstractions, custom bootstrap attributes, provider selectors,
  lifecycle aliases, custom configuration, or a generic sample runtime.

## Coalescence decision

- **Closest pattern:** application `KoanModule` in SnapVault/Meridian.
- **Current owners:** `Program.cs` partially owns framework startup; `GardenAutomation` owns rules;
  `GardenSeederRunner` forwards startup seeding; manual `AppHost` ownership bypasses the host binder.
- **State/lifetime:** lifecycle declarations are host composition; seeding is host startup; automation executes at
  Entity write boundaries; routes are stable external contracts.
- **Hot path:** compiled Lifecycle callbacks plus Entity queries only; no runtime discovery or provider election is
  added to writes.
- **Specificity:** application composition and business policy, not framework law.
- **Disposition:** keep Entity models/controllers and domain automation; absorb composition/seeding/reporting into
  one application module; rebuild `Program.cs` to parameterless `AddKoan()`; delete manual host binding, sample
  logging/lifecycle decoration, redundant runner, unused Web.Extensions reference, and obsolete comments.
- **Target owner:** `GardenCoopModule`. Core is too broad because the policy is gardening-specific; `Program.cs` is
  too narrow because it should only host; individual models cannot own application startup/seeding.

## Exact placement

| Change | Location | Why |
|---|---|---|
| `GardenCoopModule` | `samples/guides/g1c1.GardenCoop/Initialization/` | one application composition/start/report owner |
| Garden route constants | `samples/guides/g1c1.GardenCoop/Infrastructure/` | stable external API identities without repeated literals |
| simplified host | sample `Program.cs` | canonical four-line application expression |
| strict warning repairs | owning Web.Extensions/Auth source files | sample exposed framework defects; no sample suppression |
| executable specs | `tests/Suites/Samples/Koan.Samples.GardenCoop.Tests/` | business, HTTP, facts, startup, and host assertions survive future refactors |
| current contract | sample `README.md` and R10 artifacts | claims and proof remain aligned |

## Ergonomics

- Humans see the complete Koan host in four lines, then read only garden-named entities, rules, module, and APIs.
- IntelliSense begins at Entity and controller/module base types rather than custom infrastructure.
- Coding agents have one entry point and one application-composition owner; no global-host workaround or hidden
  startup sequence must be inferred.
- The module is an earned concept: the application genuinely has composition-time lifecycle policy and startup
  seed work. A simpler CRUD sample should not copy it.

## Baseline

- Normal Release build passes.
- Strict warning-as-error build exposes three pre-existing framework-owner defects: unreachable code after the
  canonical soft-delete filter path and two stale XML references to retired Auth types.
- There is no GardenCoop executable test suite.
- `Program.cs` is already intentionally modified in the epic worktree to remove old initializer/registrar dumps;
  that change must be preserved while finishing the rebuild.

## Focused acceptance

1. `Program.cs` is the parameterless four-line host with no manual `AppHost` or sample hosting incantation.
2. One discovered application module installs automation, seeds after host binding, and reports its behavior.
3. Entity/controller source remains business-readable; stable routes are centralized; redundant bootstrap and
   project-reference parts are deleted.
4. A real host proves seeded plots/sensors/readings, one dry reminder, REST access, a new sensor reading path,
   reminder recovery, and runtime facts without framework collection failures.
5. Strict sample/dependency build is warning-free without suppressions.
6. The documented NativeAOT publish/run shape executes or the claim is corrected explicitly.
7. README, sample index, solution/test membership, docs lint, diff, and privacy gates pass.

## Outcome

GardenCoop is the first R10 graduated sample:

- `Program.cs` is the canonical four-line parameterless host;
- one discovered `GardenCoopModule` owns business composition, starter state, and startup explanation;
- manual `AppHost`, sample hosting decoration, the seeder runner, redundant Web.Extensions reference, and three
  launch scripts are deleted;
- Entity models, controllers, routes, and automation read in garden language;
- a fresh executable test drives seed → dry reminder → HTTP recovery → acknowledgement → complete facts;
- the supported project-directory command serves the dashboard and API with no external infrastructure;
- the measured win-x64 NativeAOT deployment serves the same business and facts result.

Dogfeeding found three framework-owner classes of defect and repaired each centrally:

1. strict Web/Auth warnings were corrected at their shipping projects;
2. Lifecycle's deferred predecessor could first read after persistence, so `context.Prior` is now one stable
   pre-write snapshot and the loader/semaphore type is deleted;
3. the public facts serializer depended on reflection-disabled System.Text.Json under AOT, so one source-generated
   `KoanFactJson` context now owns REST/MCP/test/native serialization.

## Evidence

| Boundary | Result |
|---|---|
| Strict owners | Core, Data.Core, Identity, Web.Extensions, Web.Auth, and GardenCoop build with 0 warnings/errors |
| Lifecycle | 10/10 focused Data.Core specs, including AfterUpsert pre-write snapshot truth |
| Facts authority | 11/11 focused Core facts/segmentation serialization specs |
| Sample contract | 1/1 fresh-host cumulative business/HTTP/facts recovery spec |
| Real JIT host | 3 plots, 3 sensors, 3 readings, 1 reminder, notification observed, no collection failure |
| README path | `/` returns 200 and the Garden Cooperative dashboard from the documented project directory |
| NativeAOT | `-p:KoanAot=true` publish succeeds; 50,507,776-byte win-x64 executable serves 3 plots, 1 reminder, facts 200/complete, notification, and no collection failure |

The native result is documented as a deployment directory, not a physical single file: `wwwroot` and SQLite's
native library remain beside the executable. Existing framework/AspNet trim-analysis warnings remain an explicit
NativeAOT guide boundary; they did not prevent the measured runtime path and are not suppressed by the sample.

## Constraints

- Controllers only; no inline endpoints.
- Entity statics only; no repository ceremony.
- No unbounded stream is introduced; current data volume uses bounded Entity queries.
- No compatibility alias, manual assembly scan, global-host assignment, or sample-specific framework workaround.
- Preserve unrelated epic changes and perform no remote publication or configuration mutation.
