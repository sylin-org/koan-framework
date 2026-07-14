# Koan.Data.Backup

Reference-driven backup and restore services for Koan entities. The package discovers opted-in
entities, writes JSON Lines payloads and a manifest into a ZIP archive, stores it through
`IStorageService`, and can restore records through the entity repository surface.

> **Maturity: experimental.** Registration, policy discovery, archive construction, and restore
> implementations exist. Current tests cover discovery ownership and fail-loud deletion only; there
> is no end-to-end backup/restore conformance lane proving supported storage and data-adapter pairs.
> Do not treat this module as a production recovery guarantee yet.

## Reference and declare intent

Referencing `Sylin.Koan.Data.Backup` is sufficient for registration through `AddKoan()`; do not add a
second backup registrar in application code. Backup participation is explicit by default:

```csharp
using Koan.Data.Backup.Attributes;
using Koan.Data.Core.Model;

[EntityBackup]
public sealed class Order : Entity<Order>
{
    public decimal Total { get; set; }
}
```

Use `[assembly: EntityBackupScope(BackupScope.All)]` when every entity in an assembly should
participate unless explicitly excluded. `[EntityBackup(Enabled = false, Reason = "derived view")]`
records an intentional exclusion.

## Invoke the current API

```csharp
using Koan.Data.Backup.Extensions;

var manifest = await DataBackup.BackupTo<Order, string>(
    "orders-nightly",
    "Nightly order snapshot",
    cancellationToken);

await DataBackup.RestoreFrom<Order, string>(
    "orders-nightly",
    new RestoreOptions { DryRun = true },
    cancellationToken);

var backups = await DataBackup.ListBackups<Order, string>(cancellationToken);
```

`IBackupService`, `IRestoreService`, and `IBackupDiscoveryService` expose global, selective,
progress, viability, validation, and catalog operations for orchestration code. Per-operation options
select the storage profile, partition, batch size, compression, verification, and restore posture.

## Important boundaries

- Archives are assembled in a `MemoryStream` before upload. Entity enumeration is streaming, but the
  complete compressed archive is currently held in memory; large-backup memory safety is unproven.
- `EntityBackupAttribute.Encrypt` is policy metadata today. The archive writer does not encrypt entity
  payloads, so the flag must not be presented as data-at-rest protection.
- Retention settings identify and log cleanup candidates; managed deletion is not implemented.
  `DeleteBackup(...)` always returns a faulted task with `NotSupportedException` and changes nothing.
- Progress and cancellation state are process-local. Cancellation through the service status API does
  not replace the caller's `CancellationToken` for stopping active I/O.
- Restore compatibility across schema changes, renamed types, partitions, and every adapter/storage
  combination is not currently certified.
- No `BackupPlan`, `BackupSession`, `RestoreSession`, `IDataBackupService`, or `IDataRestoreService`
  public API exists in this package.

Configuration is under `Koan:Backup`; see [`TECHNICAL.md`](TECHNICAL.md) for the current types and
workflow. Before production use, add an application-owned restore drill that writes representative
data, backs it up to the intended storage profile, restores into isolation, and verifies business
invariants.
