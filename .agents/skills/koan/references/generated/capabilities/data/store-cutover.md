---
type: REFERENCE
domain: data
title: "Move the active Entity store"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-27
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-27
  status: passed
  scope: cold-executed by an external agent on a SQLite-to-SQLite envelope against published packages (Cutover) - digest-verified copy of the default store into an empty target, durable activation pointer honored after restart, nonempty target visibly rejected with the old default left active
---

# Move the active Entity store

Copy the current default store into an empty target, verify exact logical readback, and promote the
target while ordinary Entity calls remain unchanged.

## You need

| Piece | Package | Note |
|---|---|---|
| The current Entity store | the application's existing connector | remains active until verification succeeds |
| The target store | a supported target connector | configure it as a physically distinct named source |
| Verified promotion | `Sylin.Koan.Data.Cutover` | owns planning, copy, verification, activation, and receipts |

## The constraint box

> **The constraint:** Adding another provider never moves existing data. The supported cutover
> envelope requires one host, no external writers during the window, an empty managed target, and
> graduated routes. A failed run leaves the old default active; once target mutation may have begun,
> the target is quarantined rather than trusted.

## Check the envelope before running

| Situation | Route |
|---|---|
| SQLite, MongoDB, or PostgreSQL route inside the documented envelope | plan, inspect blockers, then run |
| External writers cannot stop | **does not fit** - establish an application-owned synchronization design |
| Target already contains data | **does not fit** - empty or reprovision it before cutover |
| Segmentation, transforms, custom filters, or unsupported route shape are present | `Plan()` must reject and name the correction |

## Leaves

- **Operator sequence and receipt:** [harden for production](../../recipes/harden-for-production.md)
- **Tested procedure:** [default-route cutover guide](../../guides/data/default-route-cutover.md)
- **Package contract:** mechanics and active-pointer state:
  [Cutover README](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.Data.Cutover/README.md)

Preserve `.Koan/data/active-route.json` with the deployment; activation state lives outside both
databases.
