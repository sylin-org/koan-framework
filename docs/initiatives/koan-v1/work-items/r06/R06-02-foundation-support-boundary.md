---
type: GUIDE
domain: framework
title: "R06-02 - Publish the Foundation Support Boundary"
audience: [maintainers, developers, operators, ai-agents]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: reviewed
  scope: Entity/data/composition/testing foundation boundary
---

# R06-02 — Publish the foundation support boundary

- Status: `passed`
- Depends on: R06-01
- Owner: Data reference, capability ledger, and local-provider documentation

## Meaningful result

A developer, coding agent, or reviewer can tell which small data path Koan actually stands behind
without interpreting a catalogue of every connector as universal provider support.

## Decision

- SQLite is the durable Level-1 application provider.
- InMemory is the fast, ephemeral conformance oracle.
- JSON is the zero-infrastructure fallback carried by the foundation bundle, but it is not promoted
  into the durable application claim.
- Remote and specialized providers remain extensions with independent evidence requirements.
- The boundary is a pre-1.0 verified candidate. Public package installation remains unsupported until
  a coherent `dev` publication is observed.

## Delivered evidence

The public Data reference is rebuilt around Entity grammar, exact provider roles, deterministic
selection, query/cost honesty, testing, inspection, and unsupported scenarios. It removes stale claims
that every connector was production-ready, silently interchangeable, or universally covered.

The InMemory package front door is rebuilt around its real provider identity, priority, capabilities,
ephemeral lifetime, and normal reference-driven composition. The invented `UseInMemoryStorage()`
example and “all framework features work identically” claim are removed; the missing technical
companion now records the implementation boundary.

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-15; closure recorded by the following documentation commit.
- Evidence: Data.Core 301/301 baseline; current InMemory 55/55, SQLite 15/15, and JSON 14/14; FirstUse
  and GoldenJourney source/package contracts; R06-01 parallel conformance isolation.
- Tests / validation: all three local connector suites pass from current Release assemblies; strict
  full-site docs and diff validation pass.
- Unsupported scenarios: public package installation, remote-provider certification, production
  migration/recovery, cross-provider transactions, and compatibility guarantees.
- Follow-up work: observe package publication in T7; assess the events/context/isolation ring next.
- Reviewer: Codex under the maintainer's standing autonomous approval.
