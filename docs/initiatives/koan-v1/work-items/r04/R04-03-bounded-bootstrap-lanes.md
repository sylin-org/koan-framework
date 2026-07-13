---
type: GUIDE
domain: core
title: "R04-03 - Establish Bounded Bootstrap Test Lanes"
audience: [maintainers, framework-authors, ai-agents]
status: draft
last_updated: 2026-07-13
framework_version: v0.17.0
---

# R04-03 — Establish bounded bootstrap test lanes

- Priority: P0
- Status: `pending`
- Depends on: R04-02
- Owner: Core bootstrap/testing

## User-visible failure

The focused bootstrap suite produced no test result for more than 304 seconds. A foundational
discovery failure can consume a session without identifying assembly, phase, module, or timeout.

## Personas

Developers and agents lose iteration time; maintainers cannot distinguish slow build, deadlock,
container wait, and test-harness leakage; reviewers lack a bounded composition gate.

## Current evidence

R02's routine focused invocation did not complete. Bootstrap spans source-generated registry,
manifests, assembly fallbacks, ordering, fail-fast/lenient mode, startup reporting, and pillar probes.
During R04-01, `dotnet test` returned exit code zero for a new xUnit v3 project without building a test
executable or reporting any discovered tests. An explicit build followed by the self-executing runner
reported the real one-test result. A zero exit code without a discovered/executed count is not evidence.

## Smallest meaningful fix

Partition bootstrap evidence into a fast deterministic lane and explicit infrastructure lanes. Add
per-fixture/test time budgets and diagnostic phase output. Diagnose and fix the first non-completing
path rather than raising the global timeout.

## Failure behavior

Timeout/failure identifies the active test, bootstrap phase, discovered module under activation, host
lease, and safe reproduction command. It terminates owned processes/hosts.

## Verification

- fast bootstrap lane completes within a recorded budget on clean and warm runs;
- discovery order, missing dependency, activation exception, lenient mode, and structured explanation
  have negative tests;
- container/broker lanes are explicit and skip/fail with a reason;
- no test depends on execution order or residue from another host.

## Compatibility and rollback

Test-lane changes do not alter public behavior. Runtime fixes discovered here require their own focused
compatibility note. Revert individual lane moves without removing diagnostic time bounds.

## Stop condition

Split a specific deadlock/slow provider into its owning card if it is not bootstrap infrastructure.
