---
type: ADR
domain: data
title: "DATA-0108 - Integrity-first Entity backup and recovery"
audience: [architects, maintainers, developers, ai-agents]
status: accepted
last_updated: 2026-07-18
framework_version: source-first
---

# DATA-0108 — Integrity-first Entity backup and recovery

## Context

The former `Sylin.Koan.Data.Backup` surface combined per-model and assembly attributes, reflection discovery, global
and selective backup, restore, catalog/query, validation, retention, health, progress/cancellation, adapter
optimization, static facades, manual registration, and inline HTTP diagnostics. Its focused proof covered only
single-Entity export. Restore had no executable recovery drill, malformed records could be skipped, cancellation did
not cancel work, unknown progress IDs reported completion, and failed global archives could still be published.

The package had no supported application/source consumer. Preserving those names would preserve false confidence,
not compatibility.

## Decision

Retain the package as one independently referenced operational capability and rebuild it around one scoped
`IBackupService`:

```csharp
var created = await backup.Create<Order, string>("before-import", request, ct);
var restored = await backup.Restore<Order, string>(created.StorageKey, request, ct);
```

Create streams one Entity type through provider-bounded paging into a temporary ZIP, writes a versioned manifest with
stable type/key identities, count, source partition, and logical-data SHA-256, then publishes the closed archive
through host-scoped Koan Storage. Collision-proof archive IDs are part of every key.

Restore downloads to bounded temporary storage and validates the entire ZIP, manifest, type/key identity, every JSON
record, record count, and checksum before the first upsert. It then applies bounded batches to the explicit target or
original source partition. Corrupt and mismatched archives throw `InvalidDataException` without mutation.

Standard `CancellationToken` is the only cancellation contract. Restore is explicitly upsert-based and does not
claim a transaction across batches or providers.

## Removed surface

The rebuild removes backup attributes and assembly scope, application-wide reflection discovery, global/selective
operations, static/model facades, mutable manifests and performance reports, discovery/catalog/query, retention and
hosted maintenance, health, progress/cancel facades, adapter optimization SPI, manual DI variants, inline endpoints,
ASP.NET Core, and the Newtonsoft.Json dependency.

There is no contracts package because no independent cross-module consumer exists. Future coordinated
whole-application recovery or an operator/HTTP control plane must be earned over durable Jobs, explicit authorization,
resource bounds, and real recovery drills.

## Consequences

- The common path has one service owner, two operations, explicit storage/partition/resource intent, and immutable
  receipts.
- InMemory, JSON, and Redis reject archive creation because they do not advertise provider-bounded paging.
- Archives are compressed and integrity-checked but not encrypted; storage security remains an operational choice.
- Current Entity types must remain JSON-compatible with archived records; long-term schema migration is not claimed.
- Provider failure or cancellation after restore mutation begins may leave a partial restore. Reapplying an archive is
  expected to be idempotent for stable Entity IDs.
- Focused graduation requires SQLite + Local Storage round-trip, corrupt-archive fail-before-mutation, type mismatch,
  bounded paging, cancellation, and unsupported-provider proof.
