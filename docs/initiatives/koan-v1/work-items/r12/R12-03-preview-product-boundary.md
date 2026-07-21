---
type: SPEC
domain: framework
title: "R12-03 - Compile the Preview Product Boundary"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-19
  status: tested
  scope: exact supported claims, package admission, dependency closure, and 0.20 intent
---

# R12-03 — Compile the preview product boundary

- Tranche: `T7C — 0.20 public-preview maturity`
- Status: `passed`
- Depends on: passed R12-01 version/admission contract and passed R12-02 blocker dispositions
- Unlocks: R12-04's coherent greenfield public narrative
- Owner: one generated, evidence-derived product boundary connecting guarantees to exact package owners

## Meaningful outcome

Koan has one small recommended 0.20 spine and a legible set of supported extensions. Every promoted
package owns an accepted guarantee and has a completely admitted public Koan dependency closure.
Available but non-promoted packages remain visible as demonstrated, experimental, or unassessed; they
do not inherit support from proximity or build success.

## Guardrails

- Start from accepted claims and user journeys, not from the package list.
- Promote every owner required to keep an admitted guarantee true, and only those owners.
- Set admitted owners to project-local `"version": "0.20"`; NBGV owns exact patches.
- Preserve independent lineage and the existing mixed-maturity dependency policy.
- Generate/check the boundary from repository truth; do not introduce a maintained package allowlist.
- Do not rewrite the public curriculum here; R12-04 consumes the accepted boundary.
- Use focused graph/version/packaging evidence. The next full candidate belongs after narrative convergence.

## Discovery order

1. Reconcile the 35-package R12-01 assessment slate with R12-02's terminal PMC decisions.
2. Define the smallest supported-foundation claim and the independently useful supported-extension claims.
3. Compute every claim's exact public Koan dependency closure and resolve any maturity leak.
4. Present the exact package/claim/version checkpoint before editing `version.json`.
5. Apply `0.20` only to admitted owners and prove generated product truth, dependency bands, and focused packs.

## Architecture checkpoint — exact selective 0.20 boundary

**Task:** Compile support claims, their complete public Koan dependency closure, and project-local
`0.20` intent into one self-checking product boundary.

**Application intent:** A developer chooses the small Koan foundation or one supported extension and
can trust that every Koan package behind that promise carries the same 0.20 compatibility signal. A
package outside those promises remains visibly lower-maturity even when it is available or tested.

**Public expression:** Start from `Sylin.Koan.Templates`, `Sylin.Koan.App`, or the `Sylin.Koan`
foundation bundle. Add supported extension packages by ordinary `PackageReference`. Read standard
NuGet versions plus the generated product surface; no Koan tier selector or support manifest is added.

**Guarantee/correction:** A `supported-foundation` or `supported-extension` claim may name only
project-local `0.20` owners, and every public Koan dependency of those owners must also be supported
and on `0.20`. Conversely, every active `0.20` owner must belong to a supported claim. Product-surface
compilation fails with the exact package/claim/dependency correction when any direction drifts.

**Complete intent surface:** `product/claims.json`; maturity vocabulary; all 93 evaluated package
projects; project-local `version.json`; public ProjectReference dependency graph; generated JSON and
Markdown product surfaces; package descriptions/READMEs; NBGV patch ownership; compatibility bands;
lineage/release planning; template/bundle install paths; and R12-02 terminal nonclaims.

**Public concepts:** Standard NuGet package versions and ProjectReferences plus the existing two
support maturity labels. The generated package table gains version intent and calls claim packages
“owners” rather than implying every owner must be installed directly.

**Docs read:** R12/R12-01/R12-02, capability ledger, generated product surface, all current claim
inputs, R11-07 certification, package/version engineering guidance, and package-owned presentation for
the proposed boundary.

**Code read:** `RepositoryInspector`, `VersionIntent`, `PackageProject`, `PackageGraph`,
`ProductSurfaceCompiler` and its tests, release lineage/planner tests, evaluated current inventory, and
every candidate's direct package dependencies and version owner.

**Reusing:** Extend the existing evaluated package inventory, graph, claim compiler, maturity
vocabulary, and project-local NBGV intent. The claim compiler becomes the one invariant owner; no
parallel package list or stamping script is introduced.

**Creating new:** One `VersionIntent` fact on the existing package model, dependency/version admission
checks in the product-surface compiler, and focused mutation tests. Add only claim records needed to
separate the local Communication foundation from RabbitMQ and the supported install path from the
still-unproved upgrade path.

**Coalescence:** Keep maturity in `product/claims.json`, dependency truth in evaluated
ProjectReferences, and exact patch identity in NBGV. Do not encode the 38 packages in tooling, infer
support from version alone, or make dependencies supported merely because they are dependencies.

**Ergonomics:** Users see one recommended foundation, optional supported capabilities, and honest
experiments. Maintainers change a claim and its owning `version.json`; the existing compiler names any
missing support owner, dependency leak, or stray 0.20 package immediately.

**Constraints satisfied:** guarantees drive versions; every guaranteed owner reaches 0.20; fewer
authorities; standard .NET/NuGet semantics; isolated cross-module contracts remain explicit; no
runtime architecture change; focused tooling evidence only; no publication or remote mutation.

**Risks:** The provisional 35-package slate omitted SQLite even though Templates directly depends on
it. Admitting Templates therefore requires SQLite plus `Data.Relational` and
`Data.Relational.Abstractions`; withholding those three would make the guarantee false. Support claims
must remain bounded (RabbitMQ excludes heterogeneous schema evolution; Auth excludes password/MFA and
distributed invitations; Media/Storage stays below 0.20). Adding version intent to generated product
truth must not replace exact NBGV patch identity.

### Admitted supported foundation — 14 package owners

| Guarantee | Exact owners |
|---|---|
| package-first installation | `Sylin.Koan`, `Sylin.Koan.App`, `Sylin.Koan.Templates` |
| bootstrap and runtime facts | `Sylin.Koan.Core` |
| Entity/Data semantics and inert cache contract | `Sylin.Koan.Data.Abstractions`, `Sylin.Koan.Data.Core`, `Sylin.Koan.Cache.Abstractions` |
| automatic/test local providers | `Sylin.Koan.Data.Connector.Json`, `Sylin.Koan.Data.Connector.InMemory` |
| durable SQLite path | `Sylin.Koan.Data.Relational.Abstractions`, `Sylin.Koan.Data.Relational`, `Sylin.Koan.Data.Connector.Sqlite` |
| local Entity Communication | `Sylin.Koan.Communication` |
| Entity Web API | `Sylin.Koan.Web` |

### Admitted supported extensions — 24 package owners

| Guarantee | Exact owners |
|---|---|
| RabbitMQ Communication carriage | `Sylin.Koan.Communication.Connector.RabbitMq` |
| Jobs | `Sylin.Koan.Jobs` |
| Cache runtime | `Sylin.Koan.Cache` |
| Canon | `Sylin.Koan.Canon`, `Sylin.Koan.Canon.Web` |
| field-at-rest Classification | `Sylin.Koan.Classification.Contracts`, `Sylin.Koan.Classification` |
| Web projections | `Sylin.Koan.Web.Extensions`, `Sylin.Koan.Web.OpenApi`, `Sylin.Koan.Web.Sse`, `Sylin.Koan.Web.OpenGraph` |
| authentication, identity, trust, and tenant-aware identity | `Sylin.Koan.Web.Auth.Abstractions`, `Sylin.Koan.Web.Auth`, `Sylin.Koan.Web.Auth.Server`, `Sylin.Koan.Identity`, `Sylin.Koan.Identity.Web`, `Sylin.Koan.Identity.Tenancy`, `Sylin.Koan.Security.Trust` |
| tenant isolation/admin | `Sylin.Koan.Tenancy`, `Sylin.Koan.Tenancy.Web` |
| MCP | `Sylin.Koan.Mcp`, `Sylin.Koan.Mcp.Explorer`, `Sylin.Koan.Mcp.Operations` |
| OpenTelemetry observability | `Sylin.Koan.Observability` |

### Explicitly not admitted in this wave

Media/Storage (PMC-022/027), AI/vector, remote Data providers, Backup, Testing packages, Web Admin,
ZenGarden, external authentication connectors, and every other demonstrated/experimental/unassessed
owner keep their current version line. Template installation is admitted from current package-only
evidence; later preview upgrade/recovery remains a separate `specified` claim owned by R12-07.

## Implementation and outcome

- `product/claims.json` now owns 29 assessed claims. Seven are `supported-foundation`, eleven are
  `supported-extension`, and their union is exactly 38 package owners. The other eleven claims remain
  verified, demonstrated, experimental, or specified without inheriting the support signal.
- The supported foundation is the 14-owner install/bootstrap/Entity/Data/local-provider/SQLite/local-
  Communication/Web closure. The 24-owner extension set is RabbitMQ, Jobs, Cache, Canon,
  Classification, Web projections, Auth/Identity/Trust, Tenancy, MCP, and Observability.
- Every admitted owner declares project-local `0.20` intent. Every other evaluated owner retains its
  prior line. GoldenJourney's generated composition lock now reflects the promoted versions it reads
  from the same evaluated project graph.
- The existing product-surface compiler reads version intent from evaluated package projects and
  enforces the boundary in both directions: a supported owner must be on 0.20 with a completely
  supported public Koan dependency closure, and a 0.20 owner must belong to a supported claim.
- Generated product truth exposes each package's version line and uses “package owners” for claims;
  there is no maintained 38-package allowlist, framework tier switch, or stamping mechanism.
- Package-first installation is an accepted current guarantee. Preview upgrade and interrupted-release
  recovery remain a separate specified claim for R12-07; no publication claim was added.

## Focused evidence

- Product-surface, version-intent, and package-graph tests: 41 passed, 0 failed.
- Release-lineage and release-planner tests: 39 passed, 0 failed.
- Generated surface: 29 claims, 93 active packages, 38 supported owners, 38 owners on 0.20, zero
  missing promotions, zero stray promotions, and no unsupported public dependency leak.
- `Koan.Packaging` Release build with warnings as errors: 0 warnings, 0 errors.
- Public documentation truth: 233 current files and 42 navigation targets passed.
- Structural documentation lint: 0 errors and 1,633 pre-existing/non-gating warnings.
- `git diff --check` is clean apart from Git's line-ending notices. No full release ratchet, pack,
  publication, push, tag, Release, deployment, or remote configuration mutation ran.

## Stop conditions

- Stop if a package is promoted only because another package depends on it.
- Stop if a guarantee requires an excluded or unassessed dependency without first admitting that dependency's contract.
- Stop before any version edit until the exact claim/package checkpoint is recorded.
- Stop before public narrative rewriting, publication, tags, pushes, or remote configuration.
