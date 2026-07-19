# Sylin.Koan.Data.Backup

Create and restore one integrity-checked Entity archive through Koan Storage.

## Install

```powershell
dotnet add package Sylin.Koan.Data.Backup
```

Reference a provider-bounded Data connector and a Storage provider as well. The package composes through the
application's existing `AddKoan()` call; there is no Backup-specific registration.

## Usage: create and restore

Resolve the standard DI service at the operational boundary that owns recovery:

```csharp
var backup = services.GetRequiredService<IBackupService>();

var created = await backup.Create<Order, string>(
    "orders-before-import",
    new BackupRequest
    {
        StorageProfile = "recovery",
        Partition = "customer-a",
        PageSize = 500
    },
    cancellationToken);

var restored = await backup.Restore<Order, string>(
    created.StorageKey,
    new RestoreRequest
    {
        StorageProfile = "recovery",
        BatchSize = 500
    },
    cancellationToken);
```

`Create` pages through the selected provider, writes a bounded temporary ZIP, and publishes only after the Entity
data and manifest are complete. `Restore` downloads to bounded temporary storage, validates format, Entity/key
identity, every JSON record, count, and SHA-256 before the first upsert. Its receipt identifies exactly what was
applied.

## Boundaries

- The selected Data provider must advertise provider-bounded paging. InMemory, JSON, and Redis reject before query
  or publication rather than pretending to stream.
- Restore is batched upsert, not replacement and not a cross-record/provider transaction. Provider failure or caller
  cancellation after mutation begins can leave a partial restore; retry is expected to be idempotent for Entity IDs.
- The archive contains one Entity type and one source partition. An explicit restore target overrides the original
  partition; otherwise the original partition is used.
- The archive is compressed and integrity-checked, but it is not encrypted. Use a Storage provider and operational
  policy appropriate for the data.
- There is no whole-application scan, attribute policy, retention scheduler, catalog, progress dashboard, HTTP
  control plane, or archive deletion API. Orchestrate recurring/durable work with the application's operations layer.
- Archive compatibility is format-versioned, but long-term schema migration is not promised. The current Entity type
  must remain JSON-compatible with the archived records.

See [TECHNICAL.md](TECHNICAL.md) for archive invariants and failure ordering.
