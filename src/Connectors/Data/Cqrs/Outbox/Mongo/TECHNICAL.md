---
uid: reference.modules.koan.data.cqrs.outbox.mongo
title: Koan.Data.Cqrs.Outbox.Connector.Mongo – Technical Reference
description: MongoDB-backed outbox provider for Koan CQRS pipelines, including leasing, connection resolution, and index maintenance.
since: 0.6.3
packages: [Sylin.Koan.Data.Cqrs.Outbox.Connector.Mongo]
source: src/Koan.Data.Cqrs.Outbox.Connector.Mongo/
validation:
  date: 2025-09-29
  status: verified
---

## Contract

- Provide a durable MongoDB implementation of `IOutboxStore` used by Koan.Data.Cqrs for deferred event delivery.
- Guarantee at-least-once dispatch via optimistic leasing and idempotent record updates.
- Auto-register the provider (and its factory) when the package is referenced, while exposing manual registration helpers for explicit control.
- Resolve connection strings from the Koan configuration stack so environments can reuse existing `Koan:Data:Sources` bindings or traditional `ConnectionStrings` nodes.

## Key components

| Component                                     | Responsibility                                                                                                                                          |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `MongoOutboxOptions`                          | Holds connection information, database/collection names, lease horizon (`LeaseSeconds`), and retry ceiling (`MaxAttempts`).                             |
| `MongoOutboxStore`                            | Implements `IOutboxStore` (append, dequeue with leasing, mark processed) and maintains the Mongo collection + indexes.                                  |
| `MongoOutboxRecord`                           | Internal persistence shape for outbox entries, including attempt counters, leasing fields, and optional deduplication tokens.                           |
| `MongoOutboxFactory`                          | `IOutboxStoreFactory` implementation tagged with provider priority `20`, allowing Koan’s outbox selector to favour Mongo when multiple providers exist. |
| `Initialization.KoanAutoRegistrar`            | Binds options, registers the store/factory, and reports module metadata to the boot report without requiring inline service wiring.                     |
| `MongoOutboxRegistration.AddMongoOutbox(...)` | Opt-in extension method for applications that prefer explicit registration or additional post-configuration of options.                                 |

## Workflow

1. **Bootstrap** – `KoanAutoRegistrar.Initialize` binds `MongoOutboxOptions` (scope `Koan:Cqrs:Outbox:Mongo`), registers `MongoOutboxStore`, and contributes the factory to `IOutboxStoreFactory` so discovery favours Mongo.
2. **Connection resolution** – During store construction, `OutboxConfig.ResolveConnectionString` gathers a connection string from (in order) `MongoOutboxOptions.ConnectionString`, `Koan:Data:Sources:{name}:mongo:ConnectionString`, and `ConnectionStrings:{name}` (default `mongo`). Missing values throw to catch misconfiguration early.
3. **Append** – `AppendAsync` converts `OutboxEntry` to `MongoOutboxRecord`, setting initial status `Pending`, `VisibleAt = UtcNow`, and attempt counter `0` before inserting into the collection.
4. **Dequeue** – `DequeueAsync`:
   - Filters for `Pending` entries that are visible and either unleased or expired (`LeaseUntil < now`).
   - Generates a GUID v7 lease id, sets `LeaseUntil = now + LeaseSeconds`, and increments `Attempt` atomically via `UpdateOne` per record to avoid conflicting leases.
   - Returns hydrated `OutboxEntry` objects for downstream processors.
5. **Completion** – `MarkProcessedAsync` matches by document id and transitions `Status` to `Done`, clearing lease metadata so the record is easy to audit.
6. **Index maintenance** – The constructor ensures three indexes (`Status + VisibleAt`, `LeaseUntil`, `DedupKey` unique/sparse) to support time-sliced polling and optional deduplication once keys are supplied.

## Configuration

- **Options binding** – Place settings under `Koan:Cqrs:Outbox:Mongo` or call `AddMongoOutbox(o => ...)` for code-based overrides.
- **Lease tuning** – `LeaseSeconds` controls how long a worker owns a batch before retry; align with average handler time plus a buffer.
- **Retry policy** – `MaxAttempts` is surfaced for processors; the store increments `Attempt` on lease. Koan.Data.Cqrs can transition records to `Dead` after exceeding the ceiling.
- **Collections** – Defaults to database `Koan`, collection `Outbox`; adjust per environment to avoid sharing queues across unrelated domains.
- **Diagnostics** – Boot report includes module registration and notes the Mongo provider, helping operators confirm the elected outbox at startup.

## Edge cases

- **Missing connection metadata** – If none of the configured sources yield a connection string, store construction throws, preventing silent fallbacks to the in-memory provider.
- **Concurrent dequeues** – Leases are double-checked (`UpdateOne` with lease filters); only one worker will succeed per document even when multiple find the same candidate.
- **Clock skew** – Because leasing depends on `DateTimeOffset.UtcNow`, ensure hosts have synchronized clocks to avoid premature reclaiming of leases.
- **High retry counts** – `Attempt` increments per lease; pipelines should check against `MaxAttempts` and mark entries `Dead` or escalate before the counter wraps (int32).
- **Index drift** – Manual collection drops or index loss degrade dequeue performance; the constructor recreates indexes on startup, but operators should ensure permissions allow `CreateIndex`.

## Validation notes

- Source review: `MongoOutboxStore.cs`, `MongoOutboxOptions.cs`, `MongoOutboxFactory.cs`, `MongoOutboxRegistration.cs`, `Initialization/KoanAutoRegistrar.cs` (commit snapshot 2025-09-29).
- Manual scenario testing: verified lease acquisition logic and index creation through code inspection.
- Documentation build: run `pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/build-docs.ps1 -ConfigPath docs/api/docfx.json -LogLevel Warning -Strict`.

