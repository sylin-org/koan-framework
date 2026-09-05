---
type: REFERENCE
domain: storage
title: "Entity-owned files"
audience: [ai-agents, developers]
status: current
last_updated: 2026-08-27
framework_version: v1.0.0
validation:
  date_last_tested: 2026-08-27
  status: passed
  scope: cold-executed by an external agent on the Local + SQLite path against published packages - bytes written through the configured profile and returned unchanged via StorageEntity<T>, metadata row saved through the elected Data connector, and the absent-connector correction observed verbatim before adding it
---

# Entity-owned files

Bind a `StorageEntity<T>` to a storage profile so bytes, metadata, and lifecycle stay attached to the
business object that owns them.

## You need

| Piece | Package | Note |
|---|---|---|
| Entity-owned storage runtime | `Sylin.Koan.Storage` | use `[StorageBinding(...)]` on `StorageEntity<T>` |
| Local provider | `Sylin.Koan.Storage.Connector.Local` | supported single-node filesystem path |
| Durable metadata row for the owning Entity | a Koan Data connector, e.g. `Sylin.Koan.Data.Connector.Sqlite` | without one, `.Save()` fails: `Koan Data has no provider candidates. Reference a Data connector and call AddKoan().` |
| Remote S3-compatible provider | `Sylin.Koan.Storage.Connector.S3` (not assessed, shelved) | not the recommended greenfield route |

## The constraint box

> **The constraint:** Storage needs a provider and an explicit lifecycle policy. Removing the owning
> Entity does not automatically dispose of its bytes. Local Storage is single-node and does not add
> shared coordination, encryption, backup, replication durability, or malware scanning.

## Choose the byte path

| Need | Shape | Boundary |
|---|---|---|
| Put files in and get them back unchanged | `StorageEntity<T>` with the Local connector | filesystem durability and backup remain yours |
| Resize, convert, or negotiate image variants | continue to [media derivatives](media-derivatives.md) | Storage keeps the original; Media derives |
| Shared remote object storage | no assessed connector path today | the S3 connector is shelved; do not imply support |

## Leaves

- **Build and ownership decisions:**
  [accept and serve files](../../recipes/accept-and-serve-files.md)
- **Runtime contract:** [Storage reference](../../reference/storage/index.md)
- **Connector contract:** local filesystem mechanics and limits:
  [Local connector README](https://github.com/sylin-org/koan-framework/blob/main/src/Connectors/Storage/Local/README.md)

If access matters, govern file delivery like any Entity read. An unguessable URL is not
authorization.
