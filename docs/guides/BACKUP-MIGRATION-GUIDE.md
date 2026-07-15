---
type: GUIDE
domain: data
title: "Migrating to Explicit Backup Participation"
audience: [developers, operators]
status: current
last_updated: 2026-07-15
framework_version: v0.17.0
validation:
  date_last_tested: 2026-07-15
  status: source-reviewed
  scope: backup participation attributes, inventory resolution, and diagnostic endpoints
---

# Migrating to Explicit Backup Participation

Use this guide to make every entity's backup participation decision explicit and inspectable. The
current default is opt-in: an entity participates through `[EntityBackup]` or an assembly-level
`[EntityBackupScope(BackupScope.All)]`. An entity with neither declaration remains uncovered and is
reported by backup inventory.

> **Encryption safety boundary:** `EntityBackupAttribute.Encrypt` and
> `EntityBackupScopeAttribute.EncryptByDefault` record policy intent only. The current archive writer
> writes unencrypted JSON Lines payloads, manifests, and verification entries into a ZIP archive.
> Neither flag provides data-at-rest protection, selects an encryption provider, or manages keys.
> Use independently verified application or storage encryption controls for sensitive backups.

Backup inventory makes declared coverage visible. It does not prove that an archive was published,
that every expected record was exported, that a restore will succeed, or that recovery objectives
are met. Establish those guarantees with application-owned backup and restore drills.

## Declare participation

Choose one policy per assembly.

For explicit entity-by-entity participation:

```csharp
using Koan.Data.Backup.Attributes;
using Koan.Data.Core.Model;

[EntityBackup]
public sealed class Order : Entity<Order>
{
    public decimal Total { get; set; }
}

[EntityBackup(Enabled = false, Reason = "Derived view; rebuild from orders")]
public sealed class OrderSummary : Entity<OrderSummary>
{
}
```

For assembly-wide participation with explicit exceptions:

```csharp
using Koan.Data.Backup.Attributes;

[assembly: EntityBackupScope(BackupScope.All)]
```

`BackupScope.None` is the default and can be declared when an assembly should visibly require an
attribute on every participating entity:

```csharp
[assembly: EntityBackupScope(BackupScope.None)]
```

Encryption intent can be recorded for future policy processing, but it does not encrypt the current
archive:

```csharp
// Metadata only for entities included through this assembly scope.
[assembly: EntityBackupScope(BackupScope.All, EncryptByDefault = true)]
```

An entity can override the assembly metadata:

```csharp
// Metadata only: current ZIP payloads remain unencrypted.
[EntityBackup(Encrypt = true)]
public sealed class CustomerProfile : Entity<CustomerProfile>
{
}
```

An entity-level `[EntityBackup]` supplies its own policy values. In particular, its `Encrypt` value
defaults to `false`; it does not inherit `EncryptByDefault = true` from the assembly.

## Understand resolution

| Assembly declaration | Entity declaration | Inventory result |
|---|---|---|
| None or absent | None | Uncovered; startup inventory warning |
| None or absent | `[EntityBackup]` | Included from entity metadata |
| `BackupScope.All` | None | Included from assembly metadata |
| `BackupScope.All` | `[EntityBackup]` | Included from entity metadata |
| Any | `[EntityBackup(Enabled = false, Reason = "...")]` | Explicitly excluded |

Always provide `Reason` for an explicit exclusion. An exclusion without a reason is reported as a
warning.

## Inspect startup inventory

Referencing `Sylin.Koan.Data.Backup` participates in normal `AddKoan()` composition. At startup, the
module builds an inventory and reports included, excluded, and uncovered entities. Treat every
uncovered warning as a policy decision to resolve: add `[EntityBackup]`, adopt an assembly scope, or
record an intentional exclusion with a reason.

The resolved `encrypt` value shown in boot logs or inventory is metadata, not evidence that archive
content was encrypted.

## Expose diagnostics deliberately

The inventory HTTP endpoints are not mapped automatically. Map them before using the curl commands:

```csharp
using Koan.Core;
using Koan.Data.Backup.Extensions;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddKoan();

var app = builder.Build();
app.MapBackupDiagnostics();
app.Run();
```

Mapping these routes exposes application inventory. Apply your application's authentication,
authorization, and network-access policy before exposing them outside a trusted environment.

```bash
# Resolved inclusion, exclusion, policy metadata, and warnings
curl http://localhost:5000/backup/inventory

# Coverage-policy health only; not backup, restore, or encryption health
curl http://localhost:5000/backup/inventory/health

# Rebuild the inventory after application metadata changes
curl -X POST http://localhost:5000/backup/inventory/refresh
```

`/backup/inventory/health` reports healthy when the inventory has no warnings. It does not validate
stored archives or restore viability.

## Migration checklist

- Inventory every Koan entity and decide whether it is included or intentionally excluded.
- Add entity-level attributes or one assembly-level scope; document every exclusion with `Reason`.
- Start the application and resolve all unexpected inventory warnings.
- If HTTP diagnostics are needed, call `app.MapBackupDiagnostics()` and secure the routes.
- Do not interpret `Encrypt` or `EncryptByDefault` as implemented encryption.
- Run an application-owned restore drill against representative data and the intended storage
  profile before relying on the backup path operationally.

For the module's current maturity and operational boundaries, read the
[Koan.Data.Backup overview](../../src/Koan.Data.Backup/README.md) and
[technical reference](../../src/Koan.Data.Backup/TECHNICAL.md). Report documentation or behavior
gaps through the [Koan Framework issue tracker](https://github.com/sylin-org/koan-framework/issues).
