---
type: REFERENCE
domain: data
title: "Backups and recovery"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-23
framework_version: v1.0.0
validation:
  status: not-yet-tested
  scope: docs/capabilities/data/backups.md
---

# Backups and recovery

Restore the application state that makes Entity reads truthful after loss, not merely a copy of one
database file.

## You need

| Piece | Package | Note |
|---|---|---|
| Database backup and restoration | no Koan package | use the selected provider's operational tooling |
| Local byte-storage backup | no Koan package | include every Storage profile whose durability matters |
| Cutover activation state | no additional package | preserve the active-route state beside the deployment |

The retained
[Data Backup package](https://github.com/sylin-org/koan-framework/blob/main/src/Koan.Data.Backup/README.md)
is unassessed and shelved. It is not a greenfield application choice.

## The constraint box

> **The constraint:** Koan does not provision infrastructure, own backups or disaster recovery, or
> provide platform failover. The infrastructure that owns database and filesystem durability must
> own the backup; the application must prove that the database, Entity-owned bytes, secrets or key
> custody, and active-route state restore coherently.

## Build the recovery set

| State | Why it belongs |
|---|---|
| Selected Entity database | holds the business records |
| Local Storage roots | hold Entity-owned bytes that database rows may reference |
| Classification key custody | encrypted fields are unusable without retained keys |
| `.Koan/data/active-route.json` when cutover is active | says which configured store is live |
| Provider configuration and secret references | reconnect the restored application without copying secrets into docs |

## Leaves

- **Decision guide and receipt:** [harden for production](../../recipes/harden-for-production.md)
- **Provider-owned mechanics:**
  [Data capability map](../../reference/capability-map.md#data)

A backup nobody restored is inventory, not evidence. Run a restore journey through Entity reads and
the application's meaningful public path.
