---
type: SPEC
domain: framework
title: "R11-03 - Establish Package Identity Substrate"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: source-first
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: 108-package evaluation, 4 focused quality cells, Templates pack cell, and representative Core/bundle nupkg inspection
---

# R11-03 — Establish package identity substrate

- Tranche: `T7B — package-product graduation`
- Status: `passed`
- Depends on: R11-02 foundation topology decision
- Unlocks: golden package journey and dependency-ordered family graduation

## Application intent

> Every surviving Koan package presents one recognizable identity, while its title, description, discovery terms,
> usage, and boundaries remain specific to the capability the developer chose.

## Substrate decisions

1. `icon.png` at the repository root is the sole canonical package mascot and is embedded at the nupkg root.
2. Shared tags contain only `koan`, `dotnet`, and `framework`. Capability, provider, protocol, and deployment terms
   belong to the package that makes those claims.
3. A package may publish only its own `README.md`. Missing package documentation remains a visible repair; the root
   framework README is never repackaged as if it described a narrower product.
4. Generic framework release-page text is not package release notes. Independent version and exact source provenance
   remain machine-owned; package-specific change explanation must be honest or omitted.
5. The intentionally isolated content-only Templates project declares the same icon and README contract explicitly.
   This is an isolation seam, not a second presentation policy or a copied asset.
6. Final package verification reads the nupkg, requires the declared README and icon entries, and compares mascot
   bytes with the repository authority. Evaluated MSBuild metadata alone is insufficient proof.

The mascot supplied by the product owner is a transparent 100×100 PNG of 2,810 bytes. [NuGet accepts embedded PNG or
JPEG icons up to 1 MB and recommends 128×128](https://learn.microsoft.com/nuget/reference/nuspec#icon); preserving
the supplied pixel artwork is preferable to introducing a derived, blurred identity asset solely to meet a
recommendation.

## Coalescence

Retire the legacy `resources/image/0_2.jpg` fallback and all dual-icon conditionals. Normal projects inherit one root
MSBuild policy; Templates has one explicit inclusion because it deliberately terminates root build inheritance.
Do not add package-local icon copies, icon properties, or an asset registry.

## Focused proof

- regenerate package quality and require zero missing/noncanonical icon findings;
- pack and inspect one ordinary library, one dependency-only bundle, and the isolated Templates package;
- require `icon.png` and the declared package-owned README to exist in each nupkg;
- require every embedded `icon.png` hash to equal the repository mascot hash;
- rerun focused packaging tests and the public-document truth gate.

## Acceptance

1. all active packages evaluate to `PackageIcon=icon.png`;
2. no active package falls back to the framework root README;
3. shared tags make no capability claim and generic release-page prose is absent;
4. the legacy JPEG and its conditional/tooling paths are retired;
5. representative nupkgs prove exact mascot bytes and owned README contents;
6. the quality report, product surface, initiative state, and packed bytes agree.

## Evidence

- package quality: 108 packages; zero missing/noncanonical icon findings; 36 repair-required, 65 review-required,
  7 structurally ready for human review, and 247 findings total;
- focused compiler cells: 4 passed;
- isolated Templates pack cell: passed and asserts `README.md`, `icon.png`, exact mascot SHA-256, no runtime
  dependencies, no build output, and compiled compatibility bands;
- representative `Sylin.Koan.Core` and dependency-only `Sylin.Koan` nupkgs both declare and contain `README.md`
  and `icon.png`; embedded mascot hashes equal repository SHA-256
  `508edf8742add8287791778d19b6dffa21252e32356887eaf970dec842b7670c`;
- `Koan.Packaging` builds warning-free and final pack verification now rejects a missing, renamed, or byte-divergent
  mascot and a declared-but-absent README;
- public documentation truth gate: passed across 177 current files and 37 navigation targets.
