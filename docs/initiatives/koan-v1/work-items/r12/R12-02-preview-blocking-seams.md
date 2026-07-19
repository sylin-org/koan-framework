---
type: SPEC
domain: framework
title: "R12-02 - Close Preview-Blocking Seams"
audience: [architects, maintainers, developers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-19
  status: in-progress
  scope: current-state PMC triage and dependency-ordered public-contract corrections
---

# R12-02 — Close preview-blocking seams

- Tranche: `T7C — 0.20 public-preview maturity`
- Status: `in-progress`
- Depends on: passed R12-01 selective preview/version contract
- Unlocks: an evidence-derived preview product boundary in R12-03
- Owner: fix, removal, or explicit exclusion of concerns that would make a proposed 0.20 guarantee false

## Meaningful outcome

A package is not withheld from 0.20 because an old assessment merely mentions it, and it is not
promoted while a current promise-level defect remains. Every PMC has one current disposition tied to
the accepted guarantee boundary. Repairs happen at the narrow owner of meaning; nonclaims remain
truthful; already-repaired history is closed by evidence rather than rebuilt.

## Current-state guarantee-impact map

This map is the initial triage. `repair` authorizes focused exploration, not a predetermined code
change. `phase` transfers a required proof to the child that owns its real terminal. `exclude` means
the concern does not enter the stated 0.20 guarantee; it does not mean the package or idea is broken.

| PMCs | Current disposition | 0.20 consequence |
|---|---|---|
| 001 | repair in R12-02 | Jobs cannot carry a confusing collision in its guaranteed Entity language. |
| 002, 004 | repair in R12-02 | MCP needs one canonical transport vocabulary and JSON wire contract, with a deliberate pre-0.20 compatibility decision. |
| 003, 028, 032 | close by current evidence | R11-07 built the complete Release solution with zero warnings and ran the SQLite/connector projects; the historical warning and missing-reference premises no longer hold. |
| 005 | exclude from supported workflow | Linked-worktree release execution is convenience, not the supported clean-checkout release path. |
| 006, 020 | phase to R12-06 | Live bounded progress and retained aggregate certification evidence belong to the actual publication operator path. |
| 007, 015 | repair or narrow in R12-02 | Data/Web support needs provider-correct filtering and a portable model-identity rule. Existing convergence evidence must be audited before code changes. |
| 008, 018 | exclude with non-promoted providers | Vector transaction reflection and remote provider provenance do not enter the initial local Data guarantee. |
| 009 | phase to R12-04 | The coherent-public-narrative gate owns public XML-doc/link enforcement. |
| 013, 014 | exclude with Backup | Backup remains outside the initial supported boundary; metadata-only encryption and incomplete Web operations remain explicit limits. |
| 021 | exclude from Communication guarantee | Runtime rejection for missing required event details is supported; a compile-time analyzer is not promised. |
| 022, 027 | withhold Media/Storage group pending R12-03 | Derivative lifecycle and connector-owned evidence are not silently inherited from Media's verified pipeline tests. |
| 023 | explicit Communication nonclaim | One-application RabbitMQ carriage is eligible; heterogeneous schema/rename evolution is not promised. |
| 024 | re-audit in R12-02 | Release/build fixture isolation is safety-relevant and may have been superseded by R08/R11 repairs. |
| 025 | closed by current evidence | Current source/package FirstUse uses no EventLog override and passes on Windows; .NET 10.0.8 degrades EventLog `SecurityException` without taking down console logging. Koan does not replace standard host provider ownership. |
| 026 | exclude from guarantee | Conservative analyzer impact is release-safe; output-sensitive optimization is not a preview contract. |
| 029 | phase to R12-05 | The lifecycle fix is already implemented; the next exact package-only consumer must close its remaining observation gate. |
| 030 | exclude with AI | Mixed adapter disposal ownership remains outside the initial supported package slate. |
| 033 | repaired and closed | GardenCoop C2 now passes 1/1 without Storage in its graph. The remaining generic invariant is fixed at Storage's chokepoint: availability is inert, while configuration or actual service use activates one fail-loud routing plan. |
| 034, 035 | explicit Auth/Tenancy nonclaims | Password/MFA ceremony and distributed invitation acceptance remain absent rather than falsely partial. |

## First chokepoint exploration — Storage layered activation

**Task:** Determine whether current transitive Media → Storage availability incorrectly activates
Storage routing, and if so make configuration/operation intent—not assembly presence alone—the plan
materialization boundary.

**Application intent:** An application may use Media's in-memory recipe/pipeline features without
configuring blob storage. When it configures or actually invokes Storage, Koan must compile the route
once or reject with the existing corrective profile/provider message.

**Public expression:** Reference the desired Media or Storage package, call existing `AddKoan()`, and
use existing Media/Storage APIs. No new enable flag, attribute, registrar call, or application service
is introduced.

**Guarantee/correction:** An unconfigured, unused Storage runtime is available but inactive and does
not stop the host. Declaring a Storage profile validates at startup. Resolving/using the standard
Storage service without a profile fails with the existing `Koan Storage has no profiles` correction.

**Complete intent surface:** package reference; optional `Koan:Storage:Profiles` and
`DefaultProfile`; provider references; `AddKoan()` host start; runtime-fact projection;
`IStorageService` resolution; `StorageEntity<T>` and Media storage-backed operations.

**Public concepts:** Existing standard configuration, DI resolution, package references, and runtime
facts only.

**Docs read:** `CLAUDE.md`, `NOW.md`, R12/R12-01, the complete PMC register, capability maturity
definitions, generated product surface, R11-05 Storage/Media findings, and Storage/Media package
README/TECHNICAL contracts.

**Code read:** `StorageModule`, `StorageRoutingPlan`, `StorageCompositionFacts`, `StorageOptions`,
`StorageService`, `MediaCoreModule`, `MediaCompositionFacts`, Media's Storage-based Entity/extensions,
Media Web startup fixtures, Storage routing/bootstrap specs, and the current GardenCoop C2 fixture.
Repository-wide constant/options/request/response discovery searches were completed before placement.

**Reusing:** `StorageOptions` remains the configuration owner; the DI-owned singleton
`StorageRoutingPlan` remains the one compiler and corrective failure owner; `StorageCompositionFacts`
remains the inspection owner; existing Media and Storage host tests remain the consumer oracles.

**Creating new:** No new production abstraction. Add only focused host tests for inactive availability,
configured startup validation, runtime correction, and facts if the current defect reproduces.

**Coalescence:** Keep availability and activation in `StorageModule`/`StorageCompositionFacts`. Do not
add `StorageEnabled`, an activation registry, a Media exception, or sample-only configuration.

**Ergonomics:** Pipeline-only Media users configure nothing. Storage users keep the same profile
configuration and receive the same corrective error at the earliest point their intent becomes real.

**Constraints satisfied:** business intent inward; standard DI/options/configuration; one pillar
chokepoint; no model decoration; no cross-module activation contract; no full ratchet; no remote or
private application access.

**Risks:** Deferring route compilation could hide invalid configuration, make facts resolve a service
accidentally, or weaken reference-as-intent. The implementation must still compile eagerly whenever
profile/default intent exists, keep runtime failure for unconfigured use, and distinguish available
from active without resolving the route.

## Focused proof for the first slice

- current GardenCoop C2 remains green and is recorded as historical-trigger closure, not repair proof;
- a real `AddKoan()` Media host with the transitive Storage runtime and no profile starts;
- its facts report Storage providers/availability without a selected route;
- resolving standard `IStorageService` without a profile fails correctively;
- configured Storage still compiles at startup and existing route/fact tests remain green;
- affected Storage, Media Web, and Data.AI tests only; no complete release ratchet.

## First slice outcome

PMC-033 is closed without a new public concept. `StorageOptions` now answers whether routing intent is
declared; `StorageModule` eagerly compiles only declared configuration; actual `IStorageService`
resolution retains the same corrective no-profile failure; and `StorageCompositionFacts` reports
inactive availability without resolving a route. Focused Release evidence passes Media Web 8/8,
Storage Core 20/20, bootstrap pillars 13/13, Data.AI 87/87, and GardenCoop C2 1/1. Configured Media/Storage
paths remain covered by the same suites. No full ratchet or remote action ran.

PMC-025 is also closed without production change. The current application probe contains no EventLog
override; the exact R08-05 candidate, R11-07 public contract, and a fresh source FirstUse proof pass on
Windows. Inspection of the exact Microsoft.Extensions.Logging.EventLog 10.0.8 binary confirms that a
`SecurityException` disables only that sink. Koan therefore retains standard host logging ownership
instead of removing user providers or adding a Koan-specific toggle for a stale premise.

The next dependency-ordered exploration is PMC-001's Jobs Entity-language collision. MCP public shape
corrections follow; Data/Web parity and model identity follow their current convergence audit.

## Stop conditions

- Stop if a repair adds a second activation system or an application-facing mode knob.
- Stop if a historical PMC is treated as current without reproducing or locating its remaining invariant.
- Stop if an out-of-slate package is promoted merely to satisfy dependency closure.
- Stop before package version edits; R12-03 owns the exact admitted product boundary.
