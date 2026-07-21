---
type: GUIDE
domain: observability
title: "R04-05 - Unify Composition and Error Facts"
audience: [maintainers, operators, framework-authors, ai-agents]
status: current
last_updated: 2026-07-14
framework_version: v0.17.0
---

# R04-05 — Unify composition and error facts

- Priority: P1
- Status: `passed`
- Depends on: R04-02, R04-03
- Owner: Core hosting/observability

## User-visible failure

Startup logs, composition contributors, health, lockfile checks, exceptions, and MCP inspection expose
related but incomplete facts. Best-effort reporting can disappear without a machine-readable reason.

## Personas

Developers debug prose; agents scrape logs; operators correlate separate views; reviewers cannot diff
the exact runtime decision or distinguish unknown from healthy.

## Current evidence

[ARCH-0111](../../../../decisions/ARCH-0111-unified-runtime-facts.md) establishes one host-owned,
schema-versioned, redacted fact envelope. Module activation/rejection and default data-adapter election
now produce that model once; startup output, exceptions, health, lock comparison, the gated Web facts
endpoint, and `koan://facts` project it without provider payload bags or log scraping.

## Smallest meaningful fix

Define a versioned, redacted fact envelope for discovery, dependency, election, capability, default,
degradation, rejection, health, and correction. Migrate one vertical path—module activation plus data
adapter election—through human and machine projections before broad adoption.

## Failure behavior

Fact collection records `unknown`/`collection-failed` with stable code and safe detail; renderers cannot
turn missing facts into success. Secrets/connection material are structurally excluded.

## Verification

- schema/serialization and redaction tests;
- deterministic ordering and correlation tests;
- startup log, machine endpoint/resource, health, exception, and lockfile projection agree for the
  vertical slice;
- degraded and reporter-failure fixtures remain diagnosable;
- compatibility/versioning behavior for the envelope is documented.

Accepting evidence on 2026-07-14:

- Core passes 208/208; the focused runtime-facts surface passes 4/4, including deterministic ordering,
  JSON round-trip, redaction, unknown/degraded health, and distinct host sessions;
- Core Unit passes 79/79; bootstrap Fast passes 17/17, Pillars 16/16, and Infrastructure 7/7;
- Data.Core passes 294/294, including the single-source default adapter decision and lockfile proof;
- Web WellKnown passes 3/3 and MCP conformance passes 73/73; their focused facts projections each pass
  1/1 and emit the canonical serialization;
- the relevant projects build in Release with zero errors.

## Compatibility and rollback

Keep current human output as a projection during migration; do not promise exact formatting. Version
machine-readable changes. Roll back one contributor/renderer without inventing a parallel fact source.

## Stop condition

Split if a universal schema starts absorbing provider-specific payloads instead of stable shared facts
plus owned detail.

## Accepted boundary

This card proves the shared model and one meaningful vertical slice. It does not claim exhaustive fact
coverage across every connector, capability negotiation, background service, health contributor, or
deployment topology. R04-06 extends honest negotiation without widening the shared schema into a
provider-specific payload container.
