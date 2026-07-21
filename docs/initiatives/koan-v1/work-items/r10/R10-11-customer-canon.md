---
type: SPEC
domain: framework
title: "R10-11 - Rebuild CustomerCanon around the automatic Canon pillar"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: strict Canon/sample builds; Canon unit 37/37, integration 6/6, bootstrap 1/1, CustomerCanon host 1/1
---

# R10-11 — Rebuild CustomerCanon around the automatic Canon pillar

- Tranche: `T7B — active-sample graduation`
- Status: `passed`
- Depends on: R09 semantic composition kernel; R10-09 semantic portfolio; R10-10 SnapVault graduation
- Unlocks: final R10 portfolio boundary
- Owner: one automatic Canon pillar and one deterministic customer-intake result

## Meaningful outcome

A developer defines a canonical Customer and its validation/enrichment rules, references Canon Web, calls
`AddKoan()`, and posts messy customer input. Koan discovers and compiles the pipeline once, normalizes the identity,
converges repeated email arrivals onto one canonical record, enriches its business fields, persists it locally, and
explains the model and pipeline. Invalid input returns exact validation reasons and never pollutes canonical storage.

## Application intent

“Turn messy customer arrivals into one trusted customer record; reject invalid arrivals with useful reasons.”

## Public expression

The complete host is the standard web expression:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan().AsWebApi();
var app = builder.Build();
await app.RunAsync();
```

The business types are one `Customer : CanonEntity<Customer>` with `[AggregationKey]` on email, one central
`CustomerPolicy`, and two ordinary `ICanonPipelineContributor<Customer>` implementations. The policy owns the rules;
each contributor names its phase and remains a thin adapter.
No application registrar, application module, runtime-builder call, hand-written controller, repository, provider
switch, or middleware call is required. `Koan.Canon.Web` is reference intent; JSON is the local Data provider.

The complete first action is:

```powershell
dotnet run --project samples/applications/CustomerCanon
```

Then POST one customer to the model route printed by Canon discovery. The checked-in launch profile supplies a stable
local URL; records live under `.koan/` and no external service is required.

## Guarantee and correction

Valid input runs deterministic phases in Canon order, normalizes email/name/phone/country/language, uses normalized
email as the aggregation identity, derives display name and tier, and persists one complete active canonical record.
Posting the same normalized email converges on its canonical id.

A contributor event with `StageStatus.Failed` is a failed pipeline decision, not decorative telemetry: the runtime
stops, does not execute later contributors, does not write the canonical entity or aggregation index, records the
failed observation, and returns a failed result. Canon Web projects that result as HTTP 422 with the exact phase
detail. A parked stage remains HTTP 202. Exceptions remain exceptions and are recorded before propagation.

If Canon is referenced without Web, `AddKoan()` still registers the runtime. Missing Data/default persistence fails
with the standard corrective host/provider error; Canon does not require a Web package to activate a domain runtime.

## Complete intent surface

- References: `Koan.Canon.Web`, Web, JSON Data, and the ordinary Koan host packages required by the sample.
- Code: four-line host, one canonical Entity, two domain contributors.
- Decorations: `[AggregationKey]` on the normalized identity.
- Configuration: local JSON path and a stable Development URL only.
- Context: process-local Canon runtime and the active Koan Data host.
- Runtime prerequisites: writable checkout; nothing external.

Every additional public concept is earned: `CanonEntity<T>` declares governed canonical state;
`ICanonPipelineContributor<T>` declares a business rule participating in a named Canon phase; `[AggregationKey]`
declares convergence identity. Registration mechanics are not application concepts.

## Evidence read

### Documentation

- `docs/engineering/index.md` requires Entity-first data, controller-owned HTTP, standard host composition,
  centralized constants/options, and focused evidence; it rejects the current unbounded custom tier endpoint.
- `docs/architecture/principles.md` requires contract assemblies to be inert, functional assemblies to own one module,
  structural composition to compile once, and `AddKoan()` to be sufficient for referenced capabilities.
- `docs/reference/canon/index.md` accurately bounds Canon as in-process with Data-backed persistence, but its shortest
  path uses explicit `AddCanonRuntime()` and an inline endpoint, contradicting current framework law.
- `ARCH-0058` correctly preserves Entity-first Canon, deterministic phases, unified contributors, and separate Web;
  its old two-package decision and application registration path predate the accepted contract-isolation/module law.
- `src/Koan.Canon.Domain/README.md` and `TECHNICAL.md` accurately describe persistence and host ownership, but teach
  explicit activation and expose the now-unnecessary `Domain` package distinction.

### Code

- `CanonEntity<T>` is the right Entity-centered terminal and already exposes `Canonize`; preserve it.
- `CanonRuntime` owns deterministic phase execution and persistence, but currently persists after a contributor emits
  a failed stage, making failure observational rather than corrective; rebuild that decision at this chokepoint.
- `CanonRuntimeServiceCollectionExtensions` compiles immutable runtime configuration once, but activation is called
  only by `CanonWebModule`; move functional ownership to Canon itself.
- `CanonWebModule` owns Web routes but also activates Canon and performs a bespoke `AppDomain` model scan; keep Web
  projection, delete both non-Web ownership and duplicate discovery.
- CustomerCanon currently needs `CanonSampleModule` → `CustomerPipelineRegistrar` → builder → two manually-created
  contributors plus a hand-written controller. The base model/controller/runtime already contain the required
  semantics, so those registration and HTTP layers are unearned.
- A real baseline run reached healthy readiness and complete facts, while both `/api/canon/models` and the advertised
  customer POST returned HTTP 500. The sample has no focused executable contract to expose that drift.

## Existing constants, options, and contracts

- Already exists: `CanonizationOptions`, phase/outcome/stage enums, runtime/persistence/audit contracts,
  `CanonPipelineBuilder<T>`, Web route constants, model catalog, generic controller, runtime facts, JSON provider.
- Needs to be created: inert `Koan.Canon.Contracts`; functional `Koan.Canon` with one `CanonModule`; discoverable
  Canon model and contributor markers; one boot-time discovery/compiler; one sample cumulative HTTP contract;
  application-owned tier identifiers/premium-country set; current README/requests.
- Delete: `Koan.Canon.Domain`, `ICanonRuntimeConfigurator`, Canon Web's AppDomain scanner/runtime activation,
  CustomerPipelineRegistrar, CanonSampleModule, CustomersController, explicit `AddCanonRuntime()` in common docs,
  and sample-only Observability/OpenAPI ceremony that does not earn the customer result.

## Coalescence decision

- Closest discovery pattern: `IKoanJob` plus `JobTypeRegistry`. A non-generic `[KoanDiscoverable]` marker lets the
  source-generated registry collect concrete generic implementations once without another AppDomain scan.
- Closest web projection pattern: `RestEntityRegistration` uses the central `AssemblyCache` and lets explicit
  controllers override generated ones. Canon can do better by consuming the canonical registry directly.
- Current decision owners: Domain contains both contracts and implementation; Web accidentally activates the domain
  runtime; the application manually registers pipeline types; runtime ignores failed-stage control meaning.
- Chosen specificity: Canon capability family. Core already owns generic discovery/module law; Canon owns what makes a
  model/contributor eligible, how pipelines compile, and what failed stages mean. Web owns only HTTP projection.
- Disposition: preserve Canon semantics; split contracts from function; absorb activation and pipeline discovery into
  one Canon module; rebuild failed-stage control; delete application registration/controller ceremony and bespoke scan.
- Target owner: `Koan.Canon.CanonModule` registers the runtime and compiles discovered contributor types into immutable
  per-model descriptors. `CanonRuntime` alone decides phase stop/persistence. `CanonWebModule` consumes discovered
  model types and projects the already-compiled runtime.
- Wider owner rejected: Core cannot know Canon model shape, phase order, or failed-stage meaning. Narrower owners
  rejected: Web must not activate a domain runtime, contributors must not register themselves, and the application
  must not restate framework discovery.
- State lifetime/hot path: source-generated discovery loads at boot; contributor bindings and pipelines compile once
  into the singleton runtime; canonization performs no assembly scan, service negotiation, or contributor reflection.

## Ergonomics

Human code reads Customer → validation → enrichment. IntelliSense starts from one `Koan.Canon` contract namespace,
`CanonEntity<T>`, the contributor interface, and phase enum. A coding model can infer the full application from the
base type, aggregation decoration, and contributor types without chasing a module, registrar, builder, or custom
controller. Startup/facts and `/api/canon/models` project the same discovered pipeline. Invalid input is a bounded,
machine-readable business result rather than a mysterious 500 or a secretly persisted record.

## Code placement

| New or changed code | Location | Justification |
|---|---|---|
| public Canon vocabulary | `src/Koan.Canon.Contracts/` | inert cross-module contracts and Entity semantics; no module |
| runtime, builders, default persistence/audit/contributors | `src/Koan.Canon/` | one functional capability owner |
| `CanonModule` and discovery compiler | `src/Koan.Canon/Initialization/` | one activation/compile chokepoint |
| Web catalog/controller/module corrections | `src/Koan.Canon.Web/` | optional HTTP projection only |
| customer model/rules/constants | `samples/applications/CustomerCanon/Domain/` | application business vocabulary |
| cumulative TestServer proof | `tests/Suites/Samples/Koan.Samples.CustomerCanon.Tests/` | real host, HTTP, persistence, failure, facts, clean stop |
| current public instructions | sample README and `requests.http`; Canon package/reference docs | one greenfield path |

No new repository, model registrar, controller, custom module identity, activation metadata, runtime scan, or provider
abstraction is justified.

## Constraints satisfied

- Entity and `CanonEntity<T>` remain the application data/semantic language.
- HTTP remains controller-owned; generated Canon controllers replace the custom sample controller.
- Stable customer tiers and premium-country policy receive application ownership; tunable runtime values keep typed
  options/builders.
- No large unbounded custom read remains; the by-tier `Customer.All()` endpoint is deleted.
- Contracts are inert by project structure; functional Canon and Canon Web each own exactly one module.
- Discovery and pipeline construction happen once; runtime paths consume immutable descriptors.
- Public docs, source, startup/facts, model discovery, HTTP status, tests, package graph, and lockfile must agree.

## Execution plan

1. Split and flatten Canon into `Koan.Canon.Contracts`, functional `Koan.Canon`, and optional `Koan.Canon.Web`;
   update solution/package graph without compatibility aliases.
2. Add registry-backed model/contributor discovery, deterministic contributor ordering, and one Canon module;
   remove configurator and bespoke Web scans.
3. Make failed/parked stage events control continuation, persistence, outcome, and Canon Web status at their owners;
   add focused runtime/Web tests before changing the sample.
4. Rebuild CustomerCanon to the four-line host, one aggregated Customer, one policy owner, and two auto-discovered phase adapters; delete the
   module/registrar/controller and incidental providers/ceremony.
5. Add one cumulative real-host proof for successful convergence, failed non-persistence and reasons, model/facts/
   readiness agreement, local storage, and clean stop; rewrite public/package docs and requests.
6. Run focused Canon unit/integration/bootstrap/sample evidence, strict affected builds, package graph/product-surface
   regeneration, public docs, diff/privacy checks, then run the one final R10 portfolio boundary.

## Risks and stop conditions

- The 500 baseline may expose a second framework defect. Diagnose it through the focused TestServer cell and fix its
  owner; do not hide it with a sample route.
- If source-generated discovery cannot carry closed contributor/model types deterministically, use the central
  `AssemblyCache` once inside Canon rather than revive an AppDomain scan or application registrar.
- Do not preserve `Koan.Canon.Domain` namespace/package aliases; Koan 1.0 is greenfield.
- Do not claim durable replay, distributed intake, transactions, or production authorization.
- Do not publish, push, tag, release, or mutate remote configuration in this card.

## Closure evidence

- Canon is now three honest packages: inert `Koan.Canon.Contracts`, one functional `Koan.Canon` module, and optional
  `Koan.Canon.Web` projection. No compatibility alias or inert-reference metadata remains.
- `AddKoan()` activates Canon without Web. Generated registry facts discover models and internal contributors; one
  host-owned catalog compiles deterministic pipelines without AppDomain scanning or process-global composition state.
- Failed and parked phases terminate the operation. Canonical writes and staged identity-index mutations commit only
  after every phase succeeds; a late-failure unit law prevents dangling indexes.
- CustomerCanon has the standard four-line host, two direct capability references, one canonical Entity, one policy
  owner, and two thin phase adapters. Its generated route accepts messy input, converges repeated email arrivals,
  enriches the trusted customer, returns 422 with reasons for invalid input, and never stores the rejected arrival.
- Strict builds pass 0 warnings / 0 errors for Contracts, Canon, Canon Web, and CustomerCanon. Focused evidence passes
  Canon unit 37/37, integration 6/6, non-Web bootstrap 1/1, and CustomerCanon host 1/1.
- Product truth compiles to 15 claims and 109 packages; Canon's three packages are verified by current docs and
  executable evidence. The public documentation truth gate passes across 178 current files and 36 navigation targets.
- The final R10 portfolio boundary builds all ten published applications strictly and passes all eight sample-owned
  suites: 45 passed, 2 intentional TaskGraph skips, 0 failed. Two invalid XML documentation references exposed by
  the boundary were corrected at their OpenGraph and Tenancy owners.
