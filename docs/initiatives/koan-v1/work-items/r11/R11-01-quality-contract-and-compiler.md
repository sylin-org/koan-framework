---
type: SPEC
domain: framework
title: "R11-01 - Compile Package Quality"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: source-first
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: 109-package deterministic assessment; 11 focused compiler cells; warning-clean Release build
---

# R11-01 — Compile package quality

- Tranche: `T7B — package-product graduation`
- Status: `passed`
- Depends on: R08-03 canonical product surface and R11 contract approval
- Unlocks: R11-02 exact topology disposition

## Application intent

> A maintainer, reviewer, or coding agent can ask which Koan packages have objective product-quality repairs, why,
> and where the owning source lives without manually interpreting the package graph.

## Complete expression and guarantee

```powershell
dotnet run --project tools/Koan.Packaging -- quality `
  --output docs/reference/package-quality.json `
  --markdown docs/reference/package-quality.md
```

The command is read-only. It evaluates the canonical package projects, derives ordinary artifact shape and semantic
presentation role, reads package-owned companion docs, and emits deterministic findings. Missing or inconsistent
facts fail with a correction. No reference, decoration, configuration, context, infrastructure, or release permission
is required beyond the checkout and pinned .NET toolchain.

The report distinguishes `repair-required`, `review-required`, and `structurally-ready`. None means graduated or
supported; those require R11 disposition, human review, and role-proportional consumer evidence.

## Coalescence decision

`RepositoryInspector` remains the single evaluated-project authority and `PackageGraph` remains dependency truth.
`PackageQualityCompiler` is a sibling projection to `ProductSurfaceCompiler` inside `Koan.Packaging`; it consumes
those facts and owns no package inventory. Stable quality vocabulary belongs in `PackagingConstants.PackageQuality`.

Do not add `KoanPackageRole`, package-quality attributes, YAML front matter requirements, or another human-maintained
catalog. Ambiguous role derivation becomes an R11-02 boundary review. Product surface may later summarize accepted
quality status, but claims and structural quality remain separate meanings.

## Objective first-pass checks

- package-owned README rather than the root fallback;
- package-specific title and install/reference orientation;
- visible meaningful-use and limits/correction sections as review signals, not prose proof;
- companion technical contract where non-trivial runtime/build behavior may need one;
- useful description without known placeholder language;
- package tags that do not merely repeat the complete historical universal tag set;
- standard artifact and semantic role derived deterministically;
- deterministic package and finding order.

## Ergonomics evidence

- One command produces both human and machine views.
- Package authors add no new metadata to participate.
- Finding codes are stable enough for agents and future gates; messages name corrections for people.
- The generated reference links directly to package docs and project owners.
- The compiler does not claim that heading detection can judge documentation quality.

## Acceptance

1. focused compiler tests prove deterministic order, role derivation, package-owned README detection, proportional
   technical expectations, generic-tag review, and corrective findings;
2. the command evaluates the real package graph and produces byte-stable JSON/Markdown;
3. the report contains every current package exactly once and reports its exact source project;
4. `Koan.Packaging` builds warning-free and its README/TECHNICAL contract documents the command;
5. initiative NOW/PROGRESS state and R11-02 baseline counts agree with the generated report;
6. no release certification or remote mutation occurs.

## Stop conditions

- Stop if the compiler needs a maintained list of package IDs or roles.
- Stop if a subjective signal can make a package `structurally-ready` without human review being explicit.
- Stop if quality generation changes package, lineage, release, or remote state.

## Acceptance evidence

- `PackageClassifier` centralizes standard artifact shape for product-surface and quality projections; role remains a
  derived review aid rather than package metadata.
- Repository evaluation now falls back from an empty `TargetFrameworks` property to the ordinary single
  `TargetFramework`; all 109 package/product rows expose their real target instead of an empty platform surface.
- `quality` emits deterministic JSON and Markdown for all 109 evaluated packages. Checked-in and independently
  regenerated files match byte-for-byte.
- Machine output defines each stable finding once and lets package rows carry only codes, preserving detailed
  corrections without repeating identical prose hundreds of times.
- The baseline is 37 `repair-required`, 72 `review-required`, 0 `structurally-ready`, 73 owned READMEs, 63 technical
  companions, and 578 findings. Shared icon/release-note/tag policy explains 323 of those findings; it is one substrate
  repair, not 323 package chores.
- Eleven focused PackageQuality/ProductSurface compiler tests pass. `Koan.Packaging` builds Release with zero warnings
  and errors. The public documentation truth gate passes across 179 current files and 37 navigation targets.
- Product-surface JSON/Markdown still regenerate byte-for-byte after shared shape classification. No release, lineage,
  package, branch, tag, or remote operation ran.
