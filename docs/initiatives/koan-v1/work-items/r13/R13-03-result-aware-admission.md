---
type: SPEC
domain: framework
title: "R13-03 - Make Admission Result-Aware and Bounded"
audience: [maintainers, test-authors, provider-authors, ai-agents]
status: current
last_updated: 2026-07-21
framework_version: v0.20.0
validation:
  date_last_tested: 2026-07-21
  status: passed
  scope: bounded process/result lifecycle, fail-loud host teardown, bootstrap and Forge consumers
---

# R13-03 — Make admission result-aware and bounded

- Status: `passed`
- Depends on: passed R13-02
- Unlocks: R13-04 native applicability
- Owner: one bounded admission execution/result contract reused by family-owned suites

## Entry gate

**Application intent:** A maintainer trusts an admission only when every exact required behavior ran,
passed, reached readiness where applicable, tore down cleanly, restored ambient state, and completed
within a declared deadline.

**Public expression:** A claim or focused card names a logical cell ID, owning test project/filter, and
lane. The existing family test project still owns setup, readiness, behavior, teardown, and ambient
assertions. The bounded runner executes the exact selection and emits a machine-readable result; no
application C# or universal test DSL is added.

**Guarantee/correction:** Nonzero process exit, timeout, missing TRX/result, zero results, unknown/not-
executed/skipped outcome, failed setup/readiness/execution/teardown, or failed ambient-restoration cell
is admission failure. Timeout kills only the owned process tree and cleanup always removes temporary
results. The failure names cell, project, phase, deadline, and reproduction command.

**Complete intent surface:** `test-bootstrap.ps1`, `forge-verify.ps1`, xUnit v3/TRX outputs,
`KoanIntegrationHost`, `KoanDataSpec`, AODB bases, family-owned fixtures, temporary result roots, and
exact per-cell deadlines.

**Public concepts:** Logical admission-cell ID plus standard project/filter/lane/deadline. Each exists
because reviewers need stable evidence identity and bounded execution; no test-pipeline DSL returns.

**Coalescence:** Reuse xUnit/TRX and the existing bounded bootstrap process pattern. Put generic
process/result validation at the tooling boundary, while setup/readiness/provider semantics stay in
their family suites. Rebuild exit-code-only paths; do not replace family runners with one universal
harness. `forge-verify.ps1` must consume the stronger result rule and include provider-bounded
streaming before it can certify Data/Vector admission.

**Ergonomics:** Test authors keep ordinary xUnit projects. Maintainers can run one exact command and
receive a small Passed/Failed record instead of interpreting console totals.

## Exact placement

The implementation may add a narrowly named admission runner/parser under `tools/Koan.Packaging` or
`scripts/` after a red contract proves the smaller owner. Stable IDs/options belong in packaging
constants/models when the tool owns them. Focused tests belong in `Koan.Packaging.Tests`; real host
lifecycle assertions remain in their existing test projects. No production edit begins until the red
fixture fixes the final owner choice.

## Focused verification

Prove pass, failed result, skip/not-executed, unknown outcome, missing expected cell, missing result
file, nonzero exit with a present TRX, execution timeout, process-tree cleanup, teardown failure, and
ambient restoration. Run `scripts/test-bootstrap.ps1` only after its affected behavior changes. Do
not treat output count or process exit alone as evidence.

## Stop conditions

- Stop if a required cell can skip or disappear and still produce green.
- Stop if cleanup can kill unrelated processes or teardown faults are swallowed.
- Stop if the design becomes a replacement test framework.

## Implementation and evidence

- `Koan.Packaging admission` owns one exact project/filter cell: it runs ordinary `dotnet test`,
  enforces the declared deadline, terminates only the owned process tree, parses TRX, removes its
  system-temporary result directory, and emits a machine-readable report plus reproduction command.
- `admission-results` lets an already-bounded family runner consume the same fail-closed TRX rule.
  Missing files, zero results, failed, skipped/not-executed, unknown outcomes, nonzero exits, and
  timeouts are all non-admissible.
- `test-bootstrap.ps1` retains its xUnit v3 runner and explicit-test semantics, but now emits TRX,
  treats skips as failures, validates every result through the common contract, and always cleans its
  owned temporary result root.
- `forge-verify.ps1` delegates execution and TRX truth to `Koan.Packaging admission`; record adapters
  now require the provider-bounded streaming cell in addition to Declares/Shared/Container/Database,
  while vector adapters retain their exact four-cell family contract.
- `IntegrationHost.DisposeAsync` no longer swallows host-stop faults. It disposes owned resources and
  then propagates teardown failure, retaining both stop and disposal errors when both occur.

## Acceptance result

- Outcome: PASS
- Date and commit: 2026-07-21; intentionally uncommitted local R13 slice
- Application intent and complete public expression: claim/family owners keep ordinary xUnit tests
  and name one logical cell, project/filter, lane, phase, and deadline
- Guarantee / correction: no process-only or count-only success can admit; every rejection carries
  cell/project/phase/deadline fields and a safe local reproduction command
- Coalescence disposition: one packaging result contract; family-owned runners retain their setup,
  readiness, behavior, teardown, ambient, and explicit-test semantics
- Ergonomics proof: the real Fast bootstrap command admitted 20/20 results and cleaned its result root
- Tests / validation: focused compiler/admission tests 33/33; owned process-tree deadline test 1/1;
  integration-host startup/teardown lifecycle tests 2/2; PowerShell parse clean
- Provider proof: Docker-free record/InMemory Forge result GREEN with exactly 5/5 required cells,
  including `Provider_bounded_streaming_realizes_or_fails_closed`
- Unsupported scenarios: a skipped native dependency remains non-green; the runner does not invent
  provider readiness or replace family assertions
- Follow-up work: R13-04 binds native applicability/results to the exact merge candidate
- Reviewer: pending maintainer review
