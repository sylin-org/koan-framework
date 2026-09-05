---
type: REFERENCE
domain: operations
title: "Production hardening"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-27
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-27
  status: passed
  scope: claims-validated by an external agent against live dev-branch content and nuget.org - all links resolve, all five named packages exist under exact ids, and the cutover/backup/field-protection/observability/soft-delete posture claims agree with their linked contracts
---

# Production hardening

Graduate the application that earned real usage into explicit provider, trust, secret, telemetry,
recovery, and publish postures, then record what evidence still does not exist.

## You need

| Piece | Package | Note |
|---|---|---|
| A networked store when local SQLite no longer fits | `Sylin.Koan.Data.Connector.Postgres` in the working recipe | choose from the application's real topology, not by habit |
| Verified default-store move | `Sylin.Koan.Data.Cutover` | never replace a live store by changing a reference alone |
| Telemetry pipeline | `Sylin.Koan.Observability` | export only to an owned destination |
| Field-at-rest protection (optional) | `Sylin.Koan.Classification` | production requires application-owned key custody |
| Recoverable Entity deletion (optional) | `Sylin.Koan.Data.SoftDelete` | not a backup, audit log, or retention system |

## The constraint box

> **The constraint:** Production claims require evidence against the real engine and deployment.
> Koan does not provision infrastructure, own backups or disaster recovery, or provide platform
> failover. An unassessed capability carries no guarantee merely because it installs.

## Harden in dependency order

| Step | Evidence before moving on |
|---|---|
| Graduate the store | cutover plan, verified receipt, and conformance on the real target engine |
| Close trust posture | no development identity, unscoped tenant work, or unowned production key custody |
| Move secrets | no literals or user-secrets in the shipped deployment |
| Export observability | OTLP reaches an owned destination and readiness describes external topology |
| Set publish shape | published artifact starts and serves the meaningful Entity journey |
| Close with a receipt | proved, unproved, and next claims are written plainly |

## Leaves

- **Operator procedure and receipt:** [harden for production](../../recipes/harden-for-production.md)
- **Constraint node:** [store cutover](../data/store-cutover.md)
- **Decision guide:** [backups and recovery](../data/backups.md)
- **Custody contract:** [field protection](../trust/field-protection.md)
- **Proof contract:** [proof and observability](proof-and-observability.md)

The hardening receipt is the deliverable. “Production-ready” without named evidence and gaps is not a
Koan claim.
