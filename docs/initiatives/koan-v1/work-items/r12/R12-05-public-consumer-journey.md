---
type: WORK
domain: framework
title: "R12-05 - Prove the Public 0.20 Consumer Journey"
audience: [architects, maintainers, release-engineers, ai-agents]
status: completed
last_updated: 2026-07-21
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: passed
  scope: public NuGet template install, generation, restore, build, and SQLite runtime
---

# R12-05 — Prove the public 0.20 consumer journey

- Tranche: `T7B — public product maturity`
- Status: `completed`
- Depends on: passed R12-01 through R12-04
- Unlocks: genuine public use and focused product-maturity corrections
- Owner: the shipped template source owns the package-first consumer expression; standard NuGet owns resolution

## Meaningful outcome

A new developer can install Koan from NuGet.org, generate either starter, restore it cleanly, and run
useful SQLite-backed code without repository access or Koan-specific release machinery.

## Public promise

The beginner path is ordinary .NET:

```powershell
dotnet new install Sylin.Koan.Templates
dotnet new koan-web -o TodoApi
dotnet run --project TodoApi
```

The generated projects use standard NuGet `0.20.*` patch-floating references. NuGet therefore selects
the latest compatible 0.20 fix without crossing into 0.21. Applications that require exact
reproducibility can use ordinary NuGet locking.

No release manifest, token replacement, generated-project correction, Koan version API, or extra
configuration participates. The content-only template package is the sole source owner.

## Correction

Public `Sylin.Koan.Templates 0.20.5` generated bounded references beginning at an unpublished
`0.20.0`, so restore selected the available 0.20.4 packages but emitted NU1603. The template source
now uses `0.20.*` directly. This expresses the intended compatibility policy and removes the false
warning without adding a moving part.

## Public evidence — 2026-07-21

- PR `#94` merged to `main` as `cfb60f848653686278a1976dcacc71386f4cb19e`.
- `Release packages` run `29796113330` packed and published successfully.
- NuGet.org indexed `Sylin.Koan.Templates 0.20.6`.
- A fresh custom template hive installed exactly 0.20.6 from NuGet.org and generated both `koan-web`
  and `koan-console`.
- Both generated projects contained only `0.20.*` Koan references.
- Fresh NuGet.org-only restores used a clean package directory and completed without NU1603 or any
  other warning.
- The web project built in Release with zero warnings and zero errors.
- The console project selected local SQLite and passed Entity save, load, and query.
- The preceding live web proof passed SQLite-backed REST create/read and
  `/.well-known/Koan/facts`.

No full release ratchet was rerun. The evidence is intentionally limited to the affected public
consumer path.

## Separate product rough edge

SQLite's zero-configuration local fallback works, but startup first reports failed service discovery
and an endpoint correction before selecting `.koan/data/Koan.sqlite`. This is not a template,
restore, or persistence failure. It is a distinct discovery/runtime explanation issue and is the
next focused product correction.

## Acceptance

1. The install/generate/run path uses public packages and ordinary .NET commands.
2. Both templates restore from NuGet.org without warnings.
3. Generated references remain within the compatible 0.20 line.
4. Web build and SQLite-backed Entity behavior pass.
5. Publication remains exclusively owned by a resulting push to `main`; `dev` activity publishes
   nothing.

All five conditions pass.
