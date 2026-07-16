---
type: GUIDE
domain: core
title: "Composition Lockfile How-To"
audience: [developers, architects]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: verified
  scope: build-time emitter + resolved twin + comparer + supported live application matches
---

# Composition Lockfile (`koan.lock.json`)

**What is this app, exactly?** — answered without booting it. Any supported package graph containing
`Sylin.Koan.Core` makes the application build emit a checked-in `koan.lock.json` describing its static
composition. The target flows through Koan's application bundles, so no direct Core reference is
required. PR review then shows composition drift in a plain `git diff`; at boot the host writes a
richer **resolved twin** and the boot report prints a one-line verdict.

## The UX

```bash
dotnet build                  # koan.lock.json is refreshed automatically
git diff koan.lock.json       # PR review now SHOWS composition drift, e.g.:
                              #   +    { "id": "Koan.Data.Connector.Postgres", "version": "0.17" }
```

At boot the report prints:

```text
Composition  7 modules · lockfile ok
```

…or, when the running composition diverged from the checked-in file:

```text
Composition  8 modules · lockfile DRIFT(+Koan.Cache)
```

## Two emitters, one schema

| File | Written by | When | Checked in? | Sections |
|---|---|---|---|---|
| `koan.lock.json` | the transitive `Sylin.Koan.Core` build target | every supported application build | **yes** | `schema`, `app`, `modules`, `directReferences` |
| `obj/koan.lock.resolved.json` | the host at boot | each run (non-production) | no (gitignored) | the above **plus** `elections`, `configKeys`, `entities`, (best-effort) `capabilities` |

The build-time file carries only what is honestly knowable **without running the app**: identity, the
resolved Koan modules it is composed of, and the Koan package/project references the application
declared directly. Adapter **elections** and negotiated **capabilities** are
runtime concerns (they depend on configuration and on what each provider negotiates — ARCH-0084), so
they live only in the resolved twin.

`app.name` is the executable assembly identity used by the build graph (for example,
`Koan.FirstUse`), not the friendly product name shown in startup reporting. Keeping those identities
separate lets an application improve its operator-facing name without creating composition drift.
The executable assembly is likewise never repeated under `modules`; that list contains referenced
Koan assemblies only.

### Build-time `koan.lock.json`

```jsonc
{
  "schema": 2,
  "app": { "name": "S5.Recs", "koan": "0.17", "tfm": "net10.0" },
  "modules": [
    { "id": "Koan.Core", "version": "0.17" },
    { "id": "Koan.Data.Connector.Postgres", "version": "0.17" }
  ],
  "directReferences": [
    { "kind": "package", "id": "Sylin.Koan.Data.Connector.Postgres" }
  ]
}
```

Versions are **major.minor** — the pre-1.0 breaking tier ([ARCH-0085](../decisions/ARCH-0085-versioning-compatibility-and-automation.md)).
This keeps a checked-in file **byte-stable across commits** (NBGV's per-commit patch height would
otherwise churn it on every build) so a diff means a *real* composition change, not noise.

### Resolved twin `obj/koan.lock.resolved.json`

```jsonc
{
  "schema": 2,
  "app": { "name": "S5.Recs", "koan": "0.17", "tfm": "net10.0" },
  "modules": [ { "id": "Koan.Core", "version": "0.17" } /* … */ ],
  "elections": { "data:default": { "adapter": "postgres", "via": "reference-priority", "priority": 14 } },
  "configKeys": [ "Koan:Data:Postgres:ConnectionString" ],
  "entities": [ { "type": "Anime", "traits": [] } ]
}
```

`configKeys` records the **keys** consumed under the `Koan:` namespace — **never the values** (a
connection string's *path* appears; its secret never does).

## Reference = Intent

Referencing a package that depends on `Sylin.Koan.Core` is the whole opt-in: NuGet imports
`buildTransitive/Sylin.Koan.Core.targets`, which writes `koan.lock.json` after `Build` for **app**
projects (`OutputType=Exe`). Force it on or off with `$(KoanComposition)`:

```xml
<PropertyGroup>
  <KoanComposition>true</KoanComposition>   <!-- or false to opt a project out -->
</PropertyGroup>
```

The two inventories answer different questions:

- `modules` is the resolved Koan assembly closure, including transitive modules needed at runtime;
- `directReferences` is only application-authored `PackageReference`/`ProjectReference` intent.

For example, an application bundle can supply `Koan.Communication` transitively, so Communication
appears under `modules`; it does not appear under `directReferences` unless the application chose that
component directly. Future automatic provider selection uses this provenance boundary rather than
guessing from loaded assemblies. Package and project identities are preserved as written, together
with their origin; connector declarations will map the identities they support.

> **Source-checkout note.** A `ProjectReference` does not import a referenced package's build assets.
> Koan's FirstUse and GoldenJourney source contracts import the same target centrally through the
> repository build, while their package-only copies receive it through `buildTransitive`. Other
> source-only framework dogfood projects must explicitly import the target or set
> `KoanComposition=false`; package consumers need no project-file ceremony.

## Drift gates

Two independent checks, each catching a different failure:

- **Build-time drift** — `scripts/compare-koan-lock.ps1` fails if a build refreshed a tracked
  `koan.lock.json` that was not committed (composition changed but not recorded). It is leg **E** of
  `scripts/green-ratchet.ps1`, so the PR gate enforces it (CI == local).
- **Boot-time drift** — the host compares the checked-in file against what actually loaded and prints
  `lockfile ok | DRIFT(<keys>)`. Drift keys read as a diff: `+Id` loaded but not locked, `-Id` locked
  but absent, `Id@ver` changed.

## Enrich the twin (advanced)

Pillars contribute their runtime-resolved sections through a discovered seam — referencing the pillar
is what makes the lockfile describe it. The data pillar ships the `data:default` election this way; a
custom pillar can add its own:

<!-- validate -->
```csharp
using System;
using Koan.Core.Composition;

// Discovered automatically (the interface is [KoanDiscoverable]); no manual registration.
public sealed class MyPillarComposition : IKoanCompositionContributor
{
    public void Contribute(KoanCompositionBuilder builder, IServiceProvider services)
    {
        builder.AddElection("search:default", adapter: "opensearch", via: "reference-priority");
        builder.AddCapability("search:opensearch", new[] { "query.filter", "vector.knn" });
        // Contributions are best-effort: never throw — a failure must not break the boot report.
    }
}
```

## Limits (by design, honest)

- **Capabilities** are negotiated per-repository (connection-bound), so the twin omits them in this
  version; query them live at `/.well-known/aggregates`.
- **Entities** reflect what was resolved by boot (entity configs populate lazily on first
  `Entity<T>` access), so the list is typically what the app touched during startup.
- The resolved twin and the boot-line comparison need a content root, so they apply to host-based apps
  (`WebApplication` / generic `Host`) in development (`ContentRootPath` = the project directory). The
  build-time `koan.lock.json` is produced for supported package applications and Koan's executable
  source contracts regardless of whether the app is later started.
- A consumer that bypasses Koan's build target has unknown direct-reference provenance. Koan does not
  infer external-provider intent from its runtime assembly closure.

## See also

- [ARCH-0085 — versioning, compatibility & automation](../decisions/ARCH-0085-versioning-compatibility-and-automation.md) (the major.minor tier)
- [ARCH-0084 — unified capability model](../decisions/ARCH-0084-unified-capability-model.md) (why elections/capabilities are runtime)
