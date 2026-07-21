---
type: SPEC
domain: framework
title: "R11-04 - Prove the Golden Package Journey"
audience: [developers, architects, maintainers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: source-first
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: entry bundles, 13 template cells, and zero-configuration Todo API source-consumer proof
---

# R11-04 — Prove the golden package journey

- Tranche: `T7B — package-product graduation`
- Status: `passed`
- Depends on: R11-03 canonical package identity
- Unlocks: dependency-ordered package-family graduation

## Application intent

> Reference Koan, add `AddKoan()`, define an `Entity<T>`, expose it through `EntityController<T>`, and receive a
> running persisted API. Each line states business or capability intent; no repository, schema, controller plumbing,
> adapter registration, or configuration ceremony is required.

## Decisions

1. `Sylin.Koan.App` is the one-reference web entry. It includes the foundation, Web, local Communication, and the
   bounded JSON provider so the three-intent application is meaningful before any infrastructure choice.
2. Adding `Sylin.Koan.Data.Connector.Sqlite` upgrades that path to durable embedded storage. Reference priority and
   the connector's local `.koan/data/Koan.sqlite` default perform the election; no application setting is required.
3. `Sylin.Koan` is the console/worker entry and owns the same immediate Entity result without the Web projection.
4. The existing `koan-web` and `koan-console` templates are the package journey. Do not add another sample, fixture
   application, bootstrap abstraction, or generated configuration file to prove the same behavior.
5. Generated source demonstrates the ideal rather than explaining the framework inline. Package/template README and
   startup facts own explanation; application code remains business-readable.
6. JSON and SQLite guarantees remain distinct and explicit. Immediate local persistence is not a claim of production
   concurrency, distributed durability, or security.

## Proof path

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
cd TodoApi
dotnet run
```

The generated project contains two ordinary package references, four bootstrap statements, one business Entity, and
one controller declaration. A POST followed by a GET proves persistence. The console template proves the same Entity
grammar without ASP.NET Core. Release clean-room proof installs the exact template nupkg and uses only staged Koan
packages plus NuGet.org dependencies.

## Coalescence

- delete both generated `appsettings.json` files and the console copy-to-output rule;
- remove tutorial comments from generated C# and project files;
- make the entry-bundle and Templates package pages carry install, meaningful result, defaults, corrections, and
  limitations;
- run the template web probe without an injected SQLite connection so the zero-configuration promise is executable.

## Acceptance

1. entry package docs show the exact smallest application and distinguish JSON from SQLite guarantees;
2. generated applications contain no Koan configuration for their default meaningful result;
3. generated source reads as business intent and remains ordinary .NET;
4. template packing rejects configuration or unresolved package-version residue;
5. focused template/package tests and representative builds pass;
6. one final release clean room remains deferred to R11-07 rather than repeated here.

## Evidence

- `Sylin.Koan`, `Sylin.Koan.App`, and `Sylin.Koan.Templates` are structurally ready and have completed focused human
  review for reference intent, defaults, meaningful result, corrections, and limitations;
- both dependency-only entry bundles build warning-free;
- all 13 `TemplatePackageCompilerTests` cells pass, including prepared ranges, canonical packed content, exact mascot,
  absent `appsettings.json`, direct-pack correction, and template shape;
- the exact `koan-web` C# sources build and run from a temporary source consumer with only App and SQLite project
  references, no configuration file, and no injected SQLite setting;
- the focused runtime reached ready, persisted `buy milk`, returned it through `EntityController<Todo>`, and created
  `.koan/data/Koan.sqlite` from the provider's autonomous default;
- package quality now reports 108 packages, 36 repair-required, 62 review-required, 10 structurally ready, and 240
  findings; the three entry surfaces have no objective findings;
- public documentation truth gate passes across 177 current files and 37 navigation targets.
