---
type: SPEC
domain: framework
title: "R08-03 - Compile One Evidence-Derived Product Surface"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-17
  status: passed
  scope: package graph, product claims, and deterministic human/machine projections
---

# R08-03 — Compile one evidence-derived product surface

- Tranche: `T7B — V1 release readiness / honest public surface`
- Status: `passed`
- Depends on: passed R08-02
- Unlocks: package-first templates, upgrade/rollback, and observed V1 release evidence
- Owner: packaging compiler; maintainers own only irreducible maturity judgment

## Meaningful outcome

A developer, coding agent, operator, or reviewer can ask one repository-owned product surface:

> “What can I install, what does it provide, where does it run, and how strongly does Koan stand behind it?”

The answer is compiled from evaluated .NET/NuGet project facts, the actual package dependency graph, owned
documentation, repository evidence, and one conservative claims file. Package existence never implies support.
Missing or contradictory evidence fails closed instead of becoming marketing prose.

Application developers do nothing. Their shortest path remains a package reference and `AddKoan()`; this is a
maintainer/release truth product, not a new application API, attribute, option, or configuration file.

## Focused exploration

**Task:** replace scattered package and maturity descriptions with one deterministic evidence-derived product
surface and delete obsolete package classification machinery.

**Application intent:** “Show me what to install, what it provides, where it runs, and how strongly Koan stands
behind it.”

**Public expression:** no application expression. Reviewers and agents run
`dotnet run --project tools/Koan.Packaging -- product-surface`; release automation invokes the same compiler.

**Guarantee/correction:** every published claim names real packages and repository-owned docs/evidence; mechanical
facts come from evaluated standard .NET/NuGet metadata. Unknown packages, duplicate claims, invalid maturity,
missing paths, root-README fallback on promoted packages, or contradictory package shape reject compilation with
a corrective error. Unclaimed packages remain visible as `unassessed`, never silently supported.

**Complete intent surface:** application developers have no additional action. Maintainers edit one claims file
only when product judgment changes; package metadata, frameworks, dependencies, and shape remain ordinary project
facts and are never restated there. Operators provide no release input.

**Public concepts:** `maturity` exists because support is a product promise that code cannot infer; `evidence` and
`documentation` exist because that promise must be reviewable. Package shape, platforms, and dependencies are not
new concepts—they are standard NuGet/MSBuild facts projected verbatim.

### Docs read

- `docs/engineering/index.md` — packable projects own README/TECHNICAL documentation and central conventions.
- `docs/architecture/principles.md` — standard .NET first, one canonical path, fail-loud correction, and no
  duplicate identity metadata are binding.
- `docs/initiatives/koan-v1/CAPABILITIES.md` — supplies the conservative maturity vocabulary and existing assessed
  claims, but its hand-maintained summary can drift.
- `docs/initiatives/koan-v1/work-items/R08-v1-release-readiness.md` — requires an evidence-backed package/provider/
  platform/maturity boundary before templates and release decisions.
- `tools/Koan.Packaging/README.md` — establishes the evaluated package graph and release compiler as the existing
  mechanical owner.

### Code read

- `RepositoryInspector.cs` — already evaluates all packable projects and is the correct mechanical fact owner;
  extend rather than create a second scanner.
- `PackageProject.cs` — carries evaluated package facts but includes obsolete `Kind`; rebuild around standard
  package shape/framework/readme ownership facts.
- `PackageGraph.cs` — already owns deterministic dependency closure and ordering; reuse unchanged.
- `ReleasePlanner.cs` and `ReleaseManifest.cs` — merely copy obsolete `Kind`; delete that inert output.
- `PackageGraphTests.cs` — closest small deterministic compiler-test pattern; extend its fixture and add focused
  product compiler tests.

### Inventory findings

- 108 independently versioned packable projects exist. All evaluate a README value, but only 71 own a package
  README; 37 silently fall back to the repository README.
- Only 61 packages currently own `TECHNICAL.md`. Availability is therefore substantially wider than support-ready
  package documentation.
- `KoanPackageKind` is explicitly informational and obsolete after ARCH-0085. Its 2/9/96 Bundle/Kernel/Periphery
  distribution does not answer a product question and has no meaningful consumer beyond release-manifest copying.
- Standard properties already distinguish template, tool, analyzer, dependency-only bundle, and ordinary library.
- Maturity and compatibility are irreducible product judgments. Inferring them from a sample/test/project name would
  overclaim, while repeating package metadata in a custom catalog would create drift.

## Coalescence decision

- **Closest pattern:** `RepositoryInspector` + `PackageGraph` + the capability evidence ledger.
- **Specificity:** mechanical package truth is repository/release tooling; maturity is repository product policy.
- **Keep:** one evaluated project scanner, one dependency graph, and the existing maturity vocabulary.
- **Absorb:** package shape/platform/readme ownership into `PackageProject`; claims validation and projections into
  one `ProductSurfaceCompiler`.
- **Rebuild:** the hand-maintained capability summary as a deterministic projection of structured claims.
- **Delete:** `KoanPackageKind`, `PackageProject.Kind`, `ReleasePackage.Kind`, root README masquerading as owned
  package documentation, and the deprecated hand-maintained module catalog.
- **Target owner:** `Koan.Packaging` owns compilation because release and inventory already share its evaluated graph.
  Core is too wide—it must not learn repository product policy. Individual modules are too narrow—they cannot
  validate cross-package evidence or support promises.

## Exact placement

| New code | Location | Justification |
|---|---|---|
| canonical irreducible claims | `product/claims.json` | one reviewable repository input without duplicated project facts |
| product surface models | `tools/Koan.Packaging/Models/ProductSurface.cs` | deterministic compiler input/output contracts |
| claims/surface constants | `tools/Koan.Packaging/Infrastructure/PackagingConstants.cs` | stable schema, paths, maturity vocabulary |
| compiler and Markdown projection | `tools/Koan.Packaging/Services/ProductSurfaceCompiler.cs` | one owner joins project graph, evidence, and public views |
| evaluated standard package facts | `PackageProject.cs` + `RepositoryInspector.cs` | extend the existing scanner rather than create another |
| command | `tools/Koan.Packaging/Program.cs` | one human/agent entry point |
| focused proofs | `tests/Koan.Packaging.Tests/ProductSurfaceCompilerTests.cs` | malformed claims and deterministic output fail closed |
| generated reference | `docs/reference/product-surface.md` | human projection from the same truth as JSON |
| architecture decision | `docs/decisions/ARCH-0118-evidence-derived-product-surface.md` | durable ownership and nonclaim |

## Ergonomics

- Developer: package presence is no longer confused with a support promise; the installable path is explicit.
- Module author: standard project metadata and owned docs are sufficient; no Koan descriptor or package-kind label.
- Coding agent: one deterministic JSON document replaces filename inference and stale prose reconciliation.
- Operator/reviewer: one command exposes package, dependency, platform, documentation, evidence, and maturity truth;
  contradictions stop the build with the exact corrective action.

## Focused acceptance

1. Standard MSBuild/NuGet facts classify package shape and platforms without `KoanPackageKind`.
2. A single claims input contains no package descriptions, frameworks, dependencies, versions, or duplicate identity.
3. Compiler tests reject unknown/duplicate claims, invalid maturity, missing docs/evidence, and promoted packages
   without an owned README.
4. Unclaimed packages appear explicitly as `unassessed`; no availability-to-support inference exists.
5. JSON and Markdown output are deterministic and generated from the same in-memory surface.
6. Release planning validates the surface automatically; operators add no input or ceremony.
7. Focused packaging tests, tool build, generated-output check, `git diff --check`, and privacy scan pass. No release
   certification or publication runs.

## Constraints satisfied

- No application API, attribute, configuration, HTTP route, entity/data-access, or runtime hot path.
- No custom identity or package metadata where standard .NET/NuGet facts exist.
- Stable schema/path/maturity values live in packaging constants.
- Documentation and ADR/TOC change with the product-policy change.
- One compiler owns all projections; no parallel catalog or compatibility crutch.

## Closure evidence

- The real evaluated graph compiles into 14 conservative capability claims and 108 installable packages.
  Standard package facts derive 102 libraries, two bundles, two analyzers, one tool, and one template.
- Thirty-seven packages correctly report a missing owned README and 88 remain explicitly `unassessed`.
  No `supported-*` claim exists before a real public release and compatibility decision.
- `product/claims.json` owns only irreducible title/summary/maturity/package/evidence/documentation judgment.
  Evaluated projects own identity, description, target frameworks, shape, docs, and dependencies.
- `product-surface` emits deterministic checked-in JSON and Markdown from one in-memory model. JSON-only
  stdout remains parseable because status narration uses stderr when no output path is supplied.
- Release planning invokes the same fail-closed compiler automatically. Unknown or duplicate claims,
  unknown maturity, missing evidence/docs, and supported packages without owned READMEs reject correction-first.
- The obsolete `KoanPackageKind` property is removed from 108 projects, the package inspector, and
  release-manifest schema 4. No replacement taxonomy or custom authoring metadata was introduced.
- The duplicate capability maturity summary is removed from `CAPABILITIES.md`; it retains detailed limits and
  evidence. The deprecated module catalog is retired in favor of the generated surface, resolving PMC-010.
- Focused product/compiler/graph/planner/release-bundle proof passes 43/43. A second real compilation byte-matches both
  checked-in projections, and stdout parses to 14 claims / 108 packages. No release certification or
  publication was run.

## Risks and stop conditions

- Stop if maturity is inferred from mere package/test/sample existence.
- Stop if the claims file repeats evaluated project facts.
- Stop if package-owned documentation is weakened to a repository README fallback.
- Stop if release automation requires an operator to supply, reconcile, or approve a second input.
- Stop before template/upgrade implementation, broad release certification, publication, tag, push, or remote mutation.
