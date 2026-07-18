---
type: SPEC
domain: framework
title: "R11-05 - Graduate Package Families"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-17
framework_version: source-first
validation:
  date_last_tested: 2026-07-17
  status: in-progress
  scope: dependency-ordered family assessment, coalescence, package prose, and focused consumer evidence
---

# R11-05 — Graduate package families

- Tranche: `T7B — package-product graduation`
- Status: `in-progress`
- Depends on: R11-04 golden package journey
- Unlocks: final packed rendering and complete release boundary

## Application intent

> Every package beyond the front door states one distinct capability addition, activates only that intent, and
> explains its smallest useful result and honest limits without requiring framework-internal knowledge.

## Method

Work in dependency order. Before editing package prose, each family receives a focused discovery pass for duplicated
mechanisms, misplaced dependencies, contract/function leakage, and opportunities to centralize policy at the concern
owner. A package is documented only after its boundary earns `keep`; a failed boundary is merged, split, renamed, or
retired first.

Each family slice updates the exact R11-02 disposition, package-specific metadata/docs, focused behavior evidence,
and generated quality report. It does not run the complete release ratchet.

## Family queue

| Family | Scope | Status |
|---|---|---|
| foundation runtime | Core, Data contracts/runtime, JSON, SQLite, Web, Communication | passed |
| contract isolation | ZenGarden, AI contracts, Vector/Media/Storage abstractions, Orchestration CLI contracts | passed |
| provider families | Data, Vector, AI, Cache, Storage, Auth, Orchestration providers | pending |
| semantic capabilities | Jobs, MCP, AI, Cache, Tenancy, Identity, Canon, Media, Classification, Security | pending |
| projections and tools | Web add-ons, testing, analyzers, generators, CLI and operator surfaces | pending |

## Foundation discovery

The package boundary review retains all seven foundation-runtime packages because each states distinct reference
intent. It also found one responsibility leak: `Data.Abstractions` referenced ASP.NET Core JSON Patch and exposed a
legacy object-shaped `PatchRequest`, while the current REST/MCP path already normalizes into canonical `PatchPayload`.

The repair removes the unused legacy request/applicator path, leaves RFC/media-type normalization in Web, and leaves
Data with one provider-neutral patch operation model. Data adapter and module authors no longer inherit HTTP patch
machinery merely to consume Entity and repository contracts.

The package review also corrected the JSON floor's corruption posture. Writes now produce a complete adjacent snapshot
before replacing the active file, cancellation is honored at the file boundary, and invalid persisted JSON throws a
corrective .NET `InvalidDataException` instead of being silently interpreted as an empty store. The package page states
its single-process, whole-file, non-transactional boundary explicitly.

## Foundation evidence

- terminal dispositions: seven `keep`; each represents a distinct composition, contract, runtime, provider, or
  projection intent;
- focused builds: Core, Data Abstractions, Data Core, JSON, SQLite, Web, and Communication — zero warnings/errors;
- patch normalization: `Koan.Web.PatchOps.Tests` 14/14 and Web JSON patch surface 5/5;
- JSON provider: connector suite 21/21, including fail-loud corrupt-store coverage;
- package artifacts: all seven Debug evidence nupkgs packed successfully and contain package-owned `README.md` plus
  canonical `icon.png` bytes; artifacts remain local evidence, not release candidates;
- generated quality: 108 packages, 36 repair-required, 57 review-required, 15 structurally ready, 231 findings; all
  seven foundation-family packages have zero structural findings;
- generated product surface: 15 claims and 108 packages agree with the evaluated graph.

This passes the foundation family only. It is not a release certification and does not promote `structurally-ready`
into a support claim.

## Contract-isolation progress

- `Sylin.Koan.ZenGarden.Core` was renamed to `Sylin.Koan.ZenGarden.Contracts`; reusable endpoint, initialization,
  tool, and capability vocabulary now states its inert role directly and functional activation remains in
  `Sylin.Koan.ZenGarden`.
- `Sylin.Koan.Orchestration.Cli.Core` was dissolved. Its public package claim was false—the containing types were
  internal and consumed only by the executable—so planning and command mechanics now live with their only runtime
  owner in `Sylin.Koan.Orchestration.Cli`. The retained Abstractions package remains the independent provider/exporter
  SPI.
- focused CLI proof: restored dependency graph, warning-free Release build, executable `--help`, and a clean tool
  package containing the owned README and canonical icon; the quality compiler now recognizes standard
  `dotnet tool install --global` first-use instructions.
- generated truth after the CLI merge: 107 packages, 34 repair-required, 56 review-required, 17 structurally ready,
  and 223 findings. The dissolved false boundary removes one package and its two findings; the retained CLI advances
  to structurally ready.
- `Sylin.Koan.AI.Contracts` and `.Contracts.Shared` both earn `keep`: the former is the inert provider/inference
  boundary and the latter is a dependency-free lifecycle exchange boundary retained by accepted cross-repository
  architecture. `AiCapability`, `IAiRecipeProvider`, and `IAiModelAdvisor` moved out of mandatory Core into the AI
  contract they describe; AI Contracts no longer has a false Core dependency. ZenGarden can describe its optional
  advisor through that inert boundary, while only an active AI runtime resolves and uses it.
- focused AI proof: Core, AI Contracts, AI runtime, ZenGarden, Ollama, and LM Studio build warning-free; AI unit
  160/160, integration 49/49, end-to-end 31/31, and ZenGarden 86/86 pass. Both contract packages pack with owned
  README and canonical icon.
- generated truth after AI contract graduation: 107 packages, 33 repair-required, 55 review-required, 19
  structurally ready, and 220 findings. Both retained contract packages now have zero structural findings.
- `Sylin.Koan.Data.Vector.Abstractions` now contains only the provider/query/schema SPI. Runtime provider election,
  per-host memoization, dependency injection, physical naming, and segmentation moved to `Sylin.Koan.Data.Vector`;
  the contracts package consequently depends only on `Sylin.Koan.Data.Abstractions`.
- `Sylin.Koan.Data.Vector` earns `keep` as the one runtime owner for entity-first facades, provider selection,
  repository caching, isolation-aware naming, participation, and health. Its package page now documents the real
  `Vector<TEntity>` surface and coordination limits instead of the former nonexistent profile/workflow examples.
- focused Vector proof: Abstractions, runtime, SearchEngine, Qdrant, Milvus, Weaviate, OpenSearch, Elasticsearch,
  and SQL Server build warning-free; Data Core vector specs pass 58/58 and the zero-infrastructure in-memory
  conformance suite passes 33/33. Synthetic adapter hosts receive the neutral segmentation realization through one
  shared test-kit chokepoint; production composition remains owned by Data Core.
- generated truth after Vector contract graduation: 107 packages, 33 repair-required, 53 review-required, 21
  structurally ready, and 216 findings. Both retained Vector packages now have zero structural findings.

### Storage and Media discovery

**Application intent:** a module or provider author can consume Storage/Media vocabulary without activating either
engine, while an application still expresses an Entity-backed media model as `Photo : MediaEntity<Photo>`.

**Complete expression:** provider authors reference `Sylin.Koan.Storage.Abstractions`; ordinary applications
reference Storage/Media functional packages or their higher bundles. `MediaEntity<TEntity>` moves to the application
namespace `Koan.Media`; no new startup call, decoration, configuration, context, or runtime prerequisite is added.

**Guarantee and correction:** contract-only references register nothing. Storage remains the owner of routing,
segmentation, provider IO, and object lifecycle; Media Core remains the owner of Entity-backed upload/dedup/read,
recipe discovery, and pipeline execution. A missing runtime or provider continues to fail at the owning functional
chokepoint rather than being simulated by a contracts assembly.

**Coalescence decision:** create one necessary `Sylin.Koan.Storage.Abstractions` boundary from the SPI already embedded
in Storage, including the service result and binding vocabulary needed across modules. Keep Storage as the runtime;
rebuild Media Abstractions as inert media/recipe vocabulary; absorb `MediaEntity<TEntity>` into Media Core; remove the
AI runtime's unused Storage and Media references. No compatibility shim or parallel namespace survives.

**Ergonomics:** application code reads `using Koan.Media; public sealed class Photo : MediaEntity<Photo>;` instead of
importing an `Abstractions.Model` namespace to obtain functional behavior. Package names, IntelliSense, runtime
activation, and agent-readable dependency intent consequently agree.

### Storage and Media evidence

- `Sylin.Koan.Storage.Abstractions` is the new inert boundary for the existing service/provider/object/binding SPI;
  its packed dependency graph contains only `Sylin.Koan.Data.Abstractions`. Storage runtime, both providers, Data
  Backup, and Media consumers now declare the exact contract dependency they compile against.
- `MediaEntity<TEntity>` moved from Media Abstractions to Media Core and the application namespace `Koan.Media`.
  Media Abstractions now depends only on Data/Storage contracts; AI dropped two unused functional dependencies.
- focused builds: Storage contracts/runtime, Local, S3, Data Backup, Media contracts/runtime/Web, AI, and the
  SnapVault dogfood application compile. The Data Backup build's stale restored `Core.Adapters` warning disappears
  after graph restore and is not a source dependency.
- focused behavior: Storage Core 3/3, Media Core 562/562, Media Web 4/4, and Storage tenant-isolation 12/12 pass.
  The legacy Local connector test project still targets obsolete option/profile/capability shapes and is assigned to
  the provider-family repair; the shipping provider itself builds warning-free. This is not concealed as provider
  graduation.
- package artifacts: Storage Abstractions, Storage, Media Abstractions, and Media Core pack with owned README and
  canonical icon; nuspec inspection confirms the two contracts packages do not depend on functional assemblies.
- generated truth: 108 packages, 33 repair-required, 50 review-required, 25 structurally ready, and 208 findings.
  All four retained Storage/Media boundaries in this slice have zero structural findings.

The contract-isolation family passes. Provider prose and the stale Local connector suite remain explicit work in the
next family; no full release certification ran here.

## Acceptance

1. every active package receives a terminal R11-02 disposition before prose graduation;
2. no contracts package depends on a functional engine or projection solely for borrowed vocabulary;
3. every survivor has package-specific identity, install/reference expression, meaningful result, and boundaries;
4. focused behavior and artifact proof is proportional to its role;
5. generated quality and product-surface truth agree after every family slice;
6. the full release ratchet remains a single R11-07 boundary.
