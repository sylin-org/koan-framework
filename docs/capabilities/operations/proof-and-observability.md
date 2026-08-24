---
type: REFERENCE
domain: operations
title: "Proof and observability"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/operations/proof-and-observability.md
---

# Proof and observability

Prove one meaningful Entity journey, inspect which providers actually participated, and make failure
actionable without reconstructing composition from source code.

## You need

| Piece | Package | Note |
|---|---|---|
| Facts, health, and `koan.lock.json` | no additional package | arrive with the foundation |
| Real-host application tests | `Sylin.Koan.Testing` · `Sylin.Koan.Testing.Hosting` | exercise the compiled Koan host |
| Real backing services in tests (optional) | `Sylin.Koan.Testing.Containers` | required when the claim depends on provider behavior |
| OpenTelemetry traces and metrics (optional) | `Sylin.Koan.Observability` | add only when export has a destination and owner |

## The constraint box

> **The constraint:** Behavior, composition, and correction are separate proofs. A green request or
> test does not prove the intended provider participated. Health is not a metric system, facts are
> not an audit log, and OpenTelemetry export does not guarantee collector availability, delivery,
> retention, or safe application-specific dimensions.

## Ask the running application

| Question | Surface |
|---|---|
| Is the process alive? | `/health/live` |
| Are selected dependencies ready? | `/health/ready` |
| What composed, and which provider won? | startup report · `/.well-known/Koan/facts` · `koan://facts` |
| What did package references compose? | `koan.lock.json` |
| Does the real public journey work? | a test through the real host and selected provider |

## Leaves

- **Build and proof recipe:** [know it works](../../recipes/know-it-works.md)
- **Test contract:** [testing guide](../../guides/testing-your-app.md)
- **Runtime contract:** [operations reference](../../reference/operations/index.md)
- **Telemetry contract:** [observability reference](../../reference/operations/observability.md)

Do not substitute a development identity, disposable store, or hidden fallback to make a production
proof green.
