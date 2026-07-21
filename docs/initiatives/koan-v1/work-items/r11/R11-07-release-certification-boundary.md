---
type: SPEC
domain: framework
title: "R11-07 - Certify the Graduated Package Boundary"
audience: [architects, maintainers, developers, operators, reviewers, ai-agents]
status: current
last_updated: 2026-07-19
framework_version: source-first
validation:
  date_last_tested: 2026-07-19
  status: passed
  scope: exact complete local public-release ratchet from commit 736b82cc3
---

# R11-07 — Certify the graduated package boundary

- Tranche: `T7B — package-product graduation`
- Status: `passed`
- Depends on: passed R11-01 through R11-06
- Unlocks: passed R11 and preparation of the exact R08-05 candidate

## Application intent

> The complete source boundary that describes Koan's 93 V1 packages builds, tests, and explains itself as one coherent
> product before any exact candidate is created or any public state is changed.

## Certification boundary

The repository-owned definition is the exact local public-release ratchet:

```powershell
pwsh scripts/green-ratchet.ps1 -Configuration Release -PublicRelease
```

It restores pinned tools; builds the complete `Koan.sln` graph with the public-release audit floor; checks composition
lockfiles; runs every runnable solution suite with bounded project concurrency and hang detection; checks broad and
public documentation truth; compiles changed instructional examples; and strictly validates skills and adapter
blueprints. `Koan.Packaging.Tests` owns the source-checkout FirstUse and GoldenJourney executable probes, while the
solution owns both applications and the packaging/template compiler.

The exact package-feed clean room remains the next R08-05 candidate operation. R11-07 must not pre-create release
lineage, versions, feeds, tags, Releases, or public packages, and must not substitute a hand-selected package list for
the canonical ratchet.

## Failure discipline

1. Run the command once from the recorded source boundary without excluding suites or weakening assertions.
2. Record every failing leg and reduce it to the owning focused cell.
3. Repair only defects proved by this boundary, using the existing owner and standard .NET concepts.
4. Rerun affected focused cells, then run the exact complete command once from the corrected boundary.
5. Preserve environment-dependent tests as visible intentional skips; absence must not become success.

## Acceptance

1. all 93 active packages retain terminal dispositions and zero generated package-quality findings;
2. the exact public-release ratchet passes every leg in Release with the public-release audit floor;
3. FirstUse and GoldenJourney reach their source-checkout business results through their canonical executable probes;
4. public documentation, package documentation, source, claims, skills, and blueprints agree;
5. no suite exclusion, warning suppression, private downstream inspection, scratch staging, publication, tag, push,
   deployment, or remote mutation is used to obtain green.

## Initial boundary and focused corrections

The first exact ratchet was red in four legs: solution build, tests, instructional code, and strict skills/blueprints.
The failures were certification drift left by already-completed architecture, not a reason to reopen those decisions:

- `Koan.sln` retained one deleted Cache analyzer-test entry. Removing that stale solution node restored the canonical
  build graph; no Cache implementation changed.
- Mongo, Postgres, and SQL Server transform specs still called the deleted process-static test helper. One shared
  adapter-surface oracle now hosts a contributor through normal DI, preserving the host-owned transform architecture.
- Data's old lockfile test and GoldenJourney rejection lane expected an invalid configured default to leave a host
  running. Both now prove the current fail-fast promise: startup stops, names the adapter and configuration key,
  suggests referencing its connector, and emits no connection-string-shaped detail.
- The broad pillar bootstrap host directly references Local Storage but did not provide its required base path. One
  shared test host now supplies an inert temporary Local profile for all pillar cells rather than weakening the
  provider's public validation.
- build-heavy Packaging probes raced over repository project `obj` paths. They now share the existing nonparallel
  executable-probe collection. Their synthetic packages publish the same semantic activation asset as real packages
  and use isolated NuGet caches, so direct-reference intent is deterministic rather than dependent on machine cache.
- the Core package probe no longer stages the shelved Orchestration package, and the Communication topology table no
  longer claims the deliberately removed Tenancy.Web edge.
- public skills and the SQL adapter blueprint were reconciled with current module identity, relationship, Media,
  Storage, Observability, semantic activation, relational-contract, and shelved Orchestration/Aspire surfaces. The
  obsolete Messaging project reference and all retired sample links were removed from validation.

Focused evidence before the corrected complete boundary:

- exact public-release solution build: zero warnings/errors;
- Classification: 55/55 (confirming the initial failures were stale binaries, with no Classification edit);
- Data fail-fast: 1/1; integration pillar bootstrap: 13/13;
- Packaging: 196/196, including GoldenJourney 2/2 and semantic/direct-reference probes;
- instructional examples: 20/20; strict skills: zero warnings; strict SQL blueprint: zero warnings.

The refreshed tracked composition lockfiles record the current relational-abstractions split and OrderIntake's
ordinary Npgsql/Redis dependency closure. They must be committed before the exact ratchet's drift leg can certify the
same boundary.

## Final certification evidence

Commit `736b82cc3` passed the exact boundary without exclusions or mutation:

```powershell
pwsh scripts/green-ratchet.ps1 -Configuration Release -PublicRelease
```

- elapsed: 1,222.2 seconds (20 minutes 22 seconds);
- public-release solution build: zero warnings/errors;
- lockfile drift: passed against the committed composition;
- solution tests: 103 projects, 4,648 passed, 30 intentional environment skips, 0 failed (4,678 total);
- Packaging: 196/196, including source-checkout FirstUse and GoldenJourney;
- broad docs: zero errors and 1,626 pre-existing non-gating warnings;
- public docs: 233 current files and 42 navigation targets;
- changed instructional examples: 20/20;
- strict skills: 20 skills, zero errors/warnings;
- strict blueprints: one blueprint, zero errors/warnings;
- final ratchet result: every leg passed.

The ratchet left no tracked drift. Its 19 retained MSBuild node-reuse workers were identified by their exact parent and
command line and stopped after the successful result; no application/test process from certification remains. No
private application was inspected and no push, publication, tag, Release, deployment, or remote setting changed.
