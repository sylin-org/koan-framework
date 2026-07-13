---
type: GUIDE
domain: observability
title: "R04-05 - Unify Composition and Error Facts"
audience: [maintainers, operators, framework-authors, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
---

# R04-05 — Unify composition and error facts

- Priority: P1
- Status: `pending`
- Depends on: R04-02, R04-03
- Owner: Core hosting/observability

## User-visible failure

Startup logs, composition contributors, health, lockfile checks, exceptions, and MCP inspection expose
related but incomplete facts. Best-effort reporting can disappear without a machine-readable reason.

## Personas

Developers debug prose; agents scrape logs; operators correlate separate views; reviewers cannot diff
the exact runtime decision or distinguish unknown from healthy.

## Current evidence

R02 found useful startup/composition code but lazy Entity inventory, caught reporting failures, and only
one focused observability test. ARCH-0105/0106 require one fact model.

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

## Compatibility and rollback

Keep current human output as a projection during migration; do not promise exact formatting. Version
machine-readable changes. Roll back one contributor/renderer without inventing a parallel fact source.

## Stop condition

Split if a universal schema starts absorbing provider-specific payloads instead of stable shared facts
plus owned detail.
