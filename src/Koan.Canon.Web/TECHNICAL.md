---
uid: reference.modules.koan.canon.web
title: Koan.Canon.Web – Technical Reference
description: MVC controllers and auto-registration for Canon projections, intake, lineage, and policy endpoints.
since: 0.6.3
packages: [Sylin.Koan.Canon.Web]
source: src/Koan.Canon.Web/
validation:
  date: 2025-09-29
  status: verified
---

## Contract

- Surface Canon HTTP endpoints (intake, admin, view inspection, policy execution, generic entity controllers) that respect Koan controller guardrails.
- Discover Canon models/value objects at runtime to generate strongly-typed REST routes without manual registration.
- Turnkey-add Canon services when `Koan:Canon:AutoRegister` is enabled (default).

## Key components

| Component | Responsibility |
| --- | --- |
| `Koan.Canon.Web.Initialization.KoanAutoRegistrar` | Registers MVC controllers, wires Canon entity/value-object controllers dynamically, and optionally calls `services.AddKoanCanon()`. Populates boot report with advertised routes. |
| `Controllers.AdminController` | Provides `/admin/replay` and `/admin/reproject` endpoints over `ICanonRuntime` (async, returns `202 Accepted`). |
| `Controllers.IntakeController` | Accepts ingestion records on `/intake/records`, persists them via `Record.Save(Constants.Sets.Intake, ct)` with generated ULIDs. |
| `Controllers.ViewsController` | Serves canonical, lineage, and arbitrary projection views. Aggregates results across discovered Canon models using `Data<TEntity,TKey>` statics and returns paged envelopes. |
| `Controllers.CanonEntityController<TModel>` / `GenericControllers` | Generic CRUD controllers routed under `/api/canon/{model}` (default prefix) and `/api/vo/{type}` for Canon value objects. |
| `Controllers.PolicyController`, `ModelsController`, `LineageController` | Supplementary REST surfaces for policy listings, model discovery, and lineage graph inspection. |

## Route map

- `/admin/replay`, `/admin/reproject`: Admin operations hitting `ICanonRuntime`.
- `/intake/records`: Intake endpoint; validates `SourceId` and stores payload, policy metadata, correlation IDs.
- `/views/{view}` and `/views/{view}/{referenceId}`: View enumeration and single-document queries with pagination envelope `{ page, size, total, hasNext, items }`.
- `/api/canon/{model}`: Generic REST controller per Canon model (auto-generated).
- `/api/vo/{type}`: CRUD controller for Canon value objects.
- Additional controllers expose `/models`, `/policies`, and `/lineage` variants (see source for exhaustive list).

## Auto-registration & discovery

1. `KoanAutoRegistrar.Initialize` calls `services.AddControllers().AddApplicationPart(...)` so ASP.NET knows about the assembly controllers.
2. Canon models (`CanonEntity<>`) are discovered by scanning loaded assemblies (`DiscoverModels`). For each model a generic controller route is registered via `Koan.Web.Extensions.GenericControllers.GenericControllers.AddGenericController`.
3. Canon value objects (`CanonValueObject<>`) follow the same pattern and are exposed under `/api/vo/{type}`.
4. Turnkey Canon wiring (`AddKoanCanon()`) runs unless `Koan:Canon:AutoRegister=false` or an `ICanonMaterializer` is already registered.
5. The auto-registrar contributes boot report entries declaring primary routes plus the auto-register flag for observability.

## Data access & paging

- Controllers rely exclusively on entity-first statics (`Record.Save`, `ProjectionView<T>.Query`, `ProjectionView<T>.All`) provided by `Koan.Data.Core`.
- `ViewsController` optimizes common queries by fetching canonical and lineage projections via deterministic document IDs (`{View}::{ReferenceId}`) before falling back to in-memory filtering.
- Simple `ReferenceId == 'value'` filters are parsed without invoking provider string-query capability, ensuring Mongo/Postgres parity.
- Paging uses in-memory slicing after aggregating per-model results; sizes are clamped to `[1, 500]`.

## Configuration & environment toggles

- `Koan:Canon:AutoRegister` (boolean, default `true`): disable if an application needs manual control over Canon service registration.
- Canon-specific options (e.g., `CanonOptions.DefaultViewName`) are respected downstream by the runtime invoked via Admin endpoints.
- Standard Koan web options (e.g., JSON configuration from `Koan.Web.Extensions`) apply automatically when the host references those packages.

## Diagnostics

- Boot report contains route inventory and auto-registration flag contributed by the auto-registrar.
- Controllers emit structured logs (e.g., `ViewsController.GetPage`) including pagination metrics and filters.
- Pair with `Koan.Recipe.Observability` to export request metrics and health endpoints; `Koan.Canon.Web` itself focuses on controller projection.

## Validation notes

- Code reviewed: `Initialization/KoanAutoRegistrar.cs`, controller implementations (Admin, Intake, Views) as of 2025-09-29.
- Verified that all controller routes depend on first-class entity statics (no repository anti-patterns).
- Confirmed auto-registration path respects `Koan:Canon:AutoRegister` and short-circuits when Canon materializers are already configured.
- Doc build validated through `docs:build` task (2025-09-29).