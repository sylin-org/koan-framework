---
type: PLAN
domain: framework
title: "Application evolution progress"
audience: [maintainers, ai-agents]
status: current
last_updated: 2026-09-05
framework_version: v1.0.0
validation:
  status: reviewed
  scope: AE-01 local package, HTTP, MCP, and browser proof; independent adoption unmeasured
---

# Application evolution progress

This file alone owns execution status. The [charter](README.md) owns scope and dependencies;
[NOW](NOW.md) owns restart instructions. Update this ledger when claiming or completing work,
with links to the actual deliverable and evidence.

## Initiative state

- Overall: active; shared-foundation milestone proved locally.
- Active implementation card: AE-01a, release the Core repair and remove local preparation.
- Next: prove published-package consumption, then AE-02 task contracts and AE-03 caller boundaries.
- Independent participants: none recruited; results and productivity measurements unavailable.

## Work ledger

| Card | Status | Deliverable / evidence / next condition |
|---|---|---|
| [AE-01 Shared foundation](work-items/01-shared-foundation.md) | done | [Applications](../../../samples/applications/SharedApprovals/README.md), [163-check receipt and limits](evidence/AE-01.md); requires locally repaired Core until publication |
| [AE-01a Published consumption](work-items/01a-published-foundation.md) | in-progress | Release existing Core repair, advance dependent floors, then remove local sample preparation and prove a fresh NuGet restore |
| [AE-02 Application evolution](work-items/02-application-evolution.md) | planned | Requires AE-01's reproducible consumer baseline |
| [AE-03 Governed agent workflow](work-items/03-governed-agent-workflow.md) | planned | Requires AE-01's shared policy and application boundary |
| [AE-04 Change review](work-items/04-change-review.md) | planned | Requires changes and behavior evidence from AE-02 and AE-03 |
| [AE-05 Incremental adoption](work-items/05-incremental-adoption.md) | planned | Requires AE-01's bounded feature/foundation |
| [AE-06 Independent validation](work-items/06-independent-validation.md) | planned | Requires internal findings, a recorded pilot decision, and consenting participants |

Use `in-progress`, `blocked`, `done`, or `stopped` as execution proceeds. A blocker names the
missing input and useful restart point; stopped work retains its findings. Front matter in
the charter and cards describes document status, not successful implementation.

## Decisions and history

### 2026-09-05 — Shared foundation proved across two real consumers

- ApprovalDesk records purchase orders; ExpenseDesk records reimbursements. Both use one approval
  lifecycle policy and separate SQLite files. Browser submission, approval, and final actions passed.
- A local NuGet fixture advanced the foundation from computed 1.0.1 (USD 1,000) to 1.0.2 (USD 500),
  then restored 1.0.1 on isolated data. Consumer source stayed unchanged. All 163 final checks passed,
  including HTTP rejection, MCP boundary behavior, matching packaged guidance, and preserved records.
- The experiment found and repaired Core's rejection of ordinary organization-owned foundation
  identities. Core repair commit: `6e2aafc56`; 35 focused tests passed. The run used local Core 1.0.34
  plus published App, SQLite, and MCP packages; it is not a nuget.org-only or launch-readiness receipt.
- [Evidence](evidence/AE-01.md) records source hashes, full package graphs, failed attempts, adoption
  work, and missing measurements. No independent participants or comparative productivity results.
- Committed application baseline: `157f9053ec10cce86154c433be119f9a2d624e0e`. Future tasks start
  from this revision and declare their own preserved contracts before edits.
- Final documentation checks: zero errors; the public documentation truth gate passed. The focused
  lint reported four existing announcement-handoff metadata warnings and five missing-front-matter
  warnings for ordinary sample/package README files. No full release certification was claimed.

### 2026-09-05 — Internal implementation authorized and AE-01 claimed

- Maintainer authorized proceeding with the internally executable work. Start with AE-01's
  shared-policy update across two distinct applications.
- Canonical working source: `samples/applications/SharedApprovals/ApprovalDesk/`; second
  consumer: `samples/applications/SharedApprovals/ExpenseDesk/`; shared package source:
  `samples/applications/SharedApprovals/Foundation/`. These paths now contain the proved implementation.
- FirstUse remains the small first-use contract; its grammar is reused without expanding it.
- The first policy experiment tightens the amount eligible for approval from USD 1,000 to
  USD 500. Purchase ordering and expense reimbursement remain consumer-owned extensions.

### 2026-09-05 — Approved initiative recorded

- Maintainer approved the proposal to capture six connected opportunities, their dependencies,
  acceptance evidence, and links to existing owners before implementation.
- Approval desk and expense requests are the working domains. A03 remains the flagship
  application/recording owner; AE-01 owns the shared foundation and second consumer.
- The initiative index and durable memory route here. Announcement guidance links the shared
  work; its existing publication and benchmark boundaries remain in force.
- No application, capability, productivity, or independent-adoption result is asserted by setup.
- Setup validation: focused documentation lint passed with zero errors and no warnings in the
  new initiative; four existing announcement-handoff metadata warnings remain. The public
  documentation truth gate passed. No runtime code changed.
