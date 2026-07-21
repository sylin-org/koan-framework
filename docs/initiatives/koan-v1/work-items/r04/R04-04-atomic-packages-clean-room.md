---
type: GUIDE
domain: packaging
title: "R04-04 - Prove Atomic Packages in an External Clean Room"
audience: [maintainers, release-engineers, ai-agents]
status: current
last_updated: 2026-07-13
framework_version: v0.17.0
---

# R04-04 — Prove atomic packages in an external clean room

- Priority: P0
- Status: `passed`
- Depends on: R04-02, R04-03
- Owner: build/release engineering

## User-visible failure

The public 0.17.0 Core/Web/SQLite set cannot restore because an internal dependency requires an
unpublished Core 0.17.3 patch. The graph also reports a high-severity advisory in the SQLite native
dependency. The package-first quickstart is unavailable.

## Personas

New developers cannot start; coding agents follow a broken copy/paste path; reviewers and operators
cannot prove that published artifacts match source or each other.

## Current evidence

R02 contains the original clean application failure, dependency chain, NU1605 result, and NU1903
advisory. The accepting 2026-07-14 proof inventories 113 independently owned package projects,
compiles an 83-identity reconciliation manifest, packs and hashes all 82 missing identities in
dependency order, and validates 78 required symbol packages with portable-PDB SourceLink documents.
The remaining identity was already public and is reconciled rather than rebuilt.

## Smallest meaningful fix

Build one local package closure from one commit/version, verify all Koan dependency ranges resolve
inside that closure, then create an application outside the repo that restores only from the produced
feed, builds, runs, performs SQLite CRUD/health, and inspects package metadata. Make this a release gate
before publishing any replacement set.

## Failure behavior

The gate reports missing/mixed package IDs and versions, the dependency requiring them, advisories,
and the atomic publish set. It must fail before partial publication.

## Verification

- version-coherence and dependency-closure tests cover every publishable package;
- external clean-room restore/build/run/CRUD succeeds without project references or repository props;
- vulnerability audit has no unaccepted high/critical advisory;
- atomic publication rehearsal and `--skip-duplicate` recovery cannot create a mixed set;
- package README, symbols, SourceLink, license, and repository metadata are inspected.

## Compatibility and rollback

Do not rewrite/delete existing public packages. Publish a new coherent patch only through the existing
operator/release gate. Compatibility ranges remain fail-loud; fix version production/publication, not
the safety bound. Roll back the release workflow before publication if any closure cell fails.

## Accepting evidence

- `Koan.Packaging.Tests`: 12/12;
- every staged package has matching ID/version/source commit, README, description, tags, license,
  repository metadata, canonical Koan compatibility ranges, symbol policy, and SHA-256 evidence;
- high/critical NuGet advisories are release errors; the SQLite closure is clean with the explicit
  safe native-library override;
- a fresh external global-package folder restores only the hydrated local Koan feed, builds, starts,
  returns healthy, and performs controller-based SQLite Entity create/read/delete;
- interrupted local packing resumes only artifacts whose embedded source commit matches the
  manifest; publication independently reconciles immutable registry identities and symbol state;
- ARCH-0110 records the accepted `dev` release contract and its honest registry/non-transactional
  boundary.

The maintainer explicitly accepted automated publication from every advancement of `dev` on
2026-07-14. No package was published during this local proof. The public front door must continue to
state that the old 0.17.0 set is incoherent until a real `dev` workflow run provides registry evidence.

This card records the terminology of its accepting proof. R07-03 supersedes package identity's
historical “source commit” shorthand: `SourceCommit` is developer provenance, while the exact
`VersionCommit` now owns package metadata, artifact identity, resumable state, and release evidence.
