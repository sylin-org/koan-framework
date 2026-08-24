---
type: REFERENCE
domain: operations
title: "Operations"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-23
  status: passed
  scope: docs/capabilities/operations.md - route table verified against leaf targets
---

# Operations

Choose how the application ships, how its real composition becomes visible, and what evidence
turns a prototype into a production claim. Compilation is supporting evidence, not the result.

## Route by need

| The request says | Fetch |
|---|---|
| "ship it as one executable" / "single file, no runtime install" | [single binary](operations/single-binary.md) |
| "what composed? why did it fail?" / "we need telemetry" | [proof and observability](operations/proof-and-observability.md) |
| "this prototype is becoming real" - providers, posture, receipt | [production hardening](operations/production-hardening.md) |

## Standing constraints

- Every capability node closes with three kinds of evidence: behavior, composition, and a useful
  correction. A claim without one of the three is a hypothesis.
- Packages publish from the release pipeline only - never from a workstation.

## Do not, at this level

- Do not claim NativeAOT support without the scheduled AOT lane's result for the target platform.
- Do not treat a development identity, disposable store, or hidden fallback as a production
  success path.

For the one-screen maturity view, see
[Proof and operations in the capability map](../reference/capability-map.md#proof-and-operations).
