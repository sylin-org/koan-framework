---
type: GUIDE
domain: core
title: "R04-03 - Establish Bounded Bootstrap Test Lanes"
audience: [maintainers, framework-authors, ai-agents]
status: draft
last_updated: 2026-07-14
framework_version: v0.17.0
---

# R04-03 — Establish bounded bootstrap test lanes

- Priority: P0
- Status: `passed`
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

Exploration then isolated the root topology problem: the single assembly referenced every offline
pillar plus Redis, ONNX, and sqlite-vec. Reference = Intent loaded that full module closure for even a
filtered Data test. The filtered test took 26.245 seconds and waited on unrelated Redis work; the full
diagnostic invocation exceeded 90 seconds.

## Smallest meaningful fix

Partition bootstrap evidence into a fast deterministic lane, an offline real-composition pillar lane,
and an explicit infrastructure lane. Add build/run time budgets and diagnostic phase output. Diagnose
and fix the first non-completing path rather than raising the global timeout.

## Landed increment — composition topology

ARCH-0109 establishes three project boundaries and `scripts/test-bootstrap.ps1`:

- Fast owns 16 Core bootstrap and test-host ownership contracts and has no pillar or infrastructure
  references.
- Pillars owns 16 real `AddKoan()` proofs using in-process backends and no Redis workaround.
- Infrastructure owns seven explicit Redis, ONNX, and sqlite-vec facts and opts out of VSTest project
  execution; explicit facts alone can still initialize their class fixture.
- The runner bounds build and execution independently, kills only its child process tree, preserves
  diagnostics, and rejects an exit-zero run without a nonzero xUnit summary.

Observed self-executing test time on the accepting host: Fast 16/16 in 0.417s, Pillars 16/16 in
4.793s, and explicit Infrastructure 7/7 in 115.178s. Infrastructure remains intentionally coarse and
internally composes all three infrastructure surfaces; split it only if that coupling causes a
concrete deadline or reliability failure.

## Failure behavior

Timeout/failure identifies the lane, build/run phase, project, deadline, and safe reproduction command;
captured verbose runner and Koan output supply the last active test and bootstrap diagnostics. It
terminates the owned child process tree without killing unrelated `dotnet` processes.

## Verification

- fast bootstrap lane completes within a recorded budget on clean and warm runs;
- discovery order, missing dependency, activation exception, lenient mode, and structured explanation
  have negative tests;
- container/broker lanes are explicit and skip/fail with a reason;
- no test depends on execution order or residue from another host.

All four conditions are satisfied. Process serialization protects the current host/registry ownership
boundary. A focused proof is red before repair because an async-owned service survives failed startup,
then green after `KoanIntegrationHost.Builder.StartAsync` disposes its wrapper before rethrowing the
original startup error. The wrapper now awaits the underlying host's asynchronous disposal path.

## Compatibility and rollback

Test-lane changes do not alter public behavior. Runtime fixes discovered here require their own focused
compatibility note. Revert individual lane moves without removing diagnostic time bounds.

## Stop condition

Split a specific deadlock/slow provider into its owning card if it is not bootstrap infrastructure.
Do not broaden the failed-start increment into general runtime host ownership already settled by
R04-02.
