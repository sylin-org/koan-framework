---
type: GUIDE
domain: packaging
title: "R04-04 - Prove Atomic Packages in an External Clean Room"
audience: [maintainers, release-engineers, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
---

# R04-04 — Prove atomic packages in an external clean room

- Priority: P0
- Status: `pending`
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

R02 contains the exact clean application probe, dependency chain, NU1605 result, and NU1903 advisory.
NBGV and bounded compatibility ranges exist, but publication was not atomic/coherent.

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

## Stop condition

Publishing or changing a support/compatibility promise requires an explicit recorded maintainer release
decision even after local proof passes.
