---
type: PLAN
domain: framework
title: "AE-05 - Incremental adoption and coexistence"
audience: [maintainers, architects, ai-agents]
status: current
last_updated: 2026-09-05
framework_version: v1.0.0
validation:
  status: reviewed
  scope: work specification; adoption and Aspire coexistence proof pending
---

# AE-05 — Incremental adoption and coexistence

Read the [charter](../README.md); claim and report work in [PROGRESS](../PROGRESS.md).
Dependency: AE-01's bounded feature/foundation. This card owns the adoption fixture and
coexistence findings; canonical guidance receives the demonstrated outcome.

## Outcome and existing evidence

Introduce one Koan feature into an existing ASP.NET Core application. Show who owns each
concern, preserve existing behavior, and demonstrate removal of the introduced boundary.
Then investigate that bounded application in an Aspire host.

Read [existing-application adoption](../../../getting-started/adopt-existing-app.md), the
[current capability map](../../../reference/capability-map.md), and current primary Aspire
documentation during execution. Old integration proposals are context, not proof of support.

## Deliver and prove

1. Establish a small conventional ASP.NET Core baseline with existing routes, persistence,
   authentication, and focused contract checks. Record its revision before adding Koan.
2. Add one approval-related feature with explicit data and route ownership. Record new
   references, configuration, external dependencies, and the boundary the application keeps.
3. Exercise both existing behavior and the new feature, including caller/tenant context where
   applicable. Detect conflicting middleware, routing, or persistence ownership.
4. Demonstrate code/configuration rollback against isolated data. State separately how new
   records are retained or exported; removing packages is not a database rollback strategy.
5. Time-box an Aspire investigation to one implementation session. Verify configuration flow,
   health, discovery, and telemetry ownership on the actual selected combination. Record
   supported-with-evidence, unsupported-with-reproduction, or deferred-with-reason; deeper
   integration needs a separately scoped decision.

## Acceptance and limits

- Existing contracts pass before adoption, after adoption, and after demonstrated rollback.
- The new feature works through the intended provider, with exact versions and limits recorded.
- Aspire findings identify the tested topology and avoid a broader compatibility promise.
- Reproduction instructions and findings feed the existing adoption guide after verification.

Redirect if adoption requires taking over unrelated application concerns or hiding a data
migration. An unsuccessful Aspire investigation can complete with a bounded finding; it cannot
be advertised as a compatible path or silently expand into orchestration replacement.
