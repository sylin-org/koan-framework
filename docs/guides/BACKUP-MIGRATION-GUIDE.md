# Koan Backup System Migration Guide

**Version:** 1.0
**Date:** 2025-10-03
**Target:** Migration from auto-discovery to attribute-based opt-in

## Overview

The Koan Backup system has migrated from automatic entity discovery to an **explicit opt-in model** using attributes. This change prevents silent data loss and provides better control over backup policies.

### What Changed

| Aspect | Before | After |
|--------|--------|-------|
| **Discovery** | All `IEntity<>` automatically backed up | Only entities with `[EntityBackup]` or assembly scope |
| **Policy** | No per-entity configuration | Encrypt, IncludeSchema, Enabled, Reason |
| **Warnings** | Silent failures (ItemCount = 0) | Startup warnings for uncovered entities |
| **Failures** | Manifests marked Completed with errors | Manifests marked Failed, restore refuses |
| **Validation** | None | Startup inventory validation with boot logs |

---

## Migration Steps

### Step 1: Review Current Entities

Identify all entities currently being backed up (previously auto-discovered):

```bash
# List all Entity<> types in your codebase
grep -r "IEntity<" src/ --include="*.cs" | grep "class"
```

### Step 2: Choose Migration Strategy

**Option A: Assembly-Level Opt-In (Recommended for Most Projects)**

Add to each assembly containing entities you want to back up:

```csharp
using Koan.Data.Backup.Attributes;

// Opt-in all entities in this assembly
[assembly: EntityBackupScope(Mode = BackupScope.All)]

namespace MyApp.Models
{
    // All Entity<> types in this assembly are now included
    public class User : Entity<User> { }
    public class Product : Entity<Product> { }
}
```

**Option B: Strict Mode (For Security-Critical Assemblies)**

Require explicit decoration on every entity:

```csharp
using Koan.Data.Backup.Attributes;

// Require explicit [EntityBackup] on each entity
[assembly: EntityBackupScope(Mode = BackupScope.None)]

namespace MyApp.SecureModels
{
    // Must explicitly opt-in
    [EntityBackup]
    public class SensitiveData : Entity<SensitiveData> { }

    // Will generate startup warning (not backed up)
    public class UnmarkedEntity : Entity<UnmarkedEntity> { }
}
```

### Step 3: Apply Entity-Level Policies

Override assembly defaults or add specific policies:

```csharp
// PII/sensitive data - enable encryption
[EntityBackup(Encrypt = true)]
public class User : Entity<User>
{
    public string Email { get; set; }
    public string PasswordHash { get; set; }
}

// High-volume logs - exclude schema to reduce size
[EntityBackup(IncludeSchema = false)]
public class LogEntry : Entity<LogEntry>
{
    public string Message { get; set; }
}

// Derived/cached data - explicit opt-out
[EntityBackup(Enabled = false, Reason = "Derived view, rebuild from source")]
public class SearchIndex : Entity<SearchIndex>
{
    public string IndexData { get; set; }
}
```

### Step 4: Validate Startup Inventory

Run your application and check boot logs:

```
[INFO] Koan:backup.inventory validation | included=12 excluded=1 warnings=0
[DEBUG] Koan:backup.inventory included | entity=User encrypt=true schema=true source=attribute
[WARN] Koan:backup   uncovered: 2 entities
[WARN] Koan:backup     UnmarkedEntity → no backup coverage (assembly scope: None)
```

**Action Required:** Address any warnings by adding `[EntityBackup]` or `[EntityBackupScope]`.

### Step 5: Update Integration Tests

If you have custom backup tests, update test entities:

```csharp
using Koan.Data.Backup.Attributes;

[assembly: EntityBackupScope(Mode = BackupScope.All)]

namespace MyApp.Tests
{
    [EntityBackup]
    public class TestEntity : Entity<TestEntity> { }
}
```

---

## Common Migration Patterns

### Pattern 1: Simple Application (One Assembly)

```csharp
// In Models/AssemblyInfo.cs or any model file
using Koan.Data.Backup.Attributes;

[assembly: EntityBackupScope(Mode = BackupScope.All)]
```

**Result:** All entities in the assembly are backed up with default policies.

### Pattern 2: Multi-Tenant with Encryption

```csharp
// Encrypt all entities by default
[assembly: EntityBackupScope(Mode = BackupScope.All, EncryptByDefault = true)]

namespace MyApp.Models
{
    // Uses assembly default (encrypted)
    public class TenantData : Entity<TenantData> { }

    // Override for public data
    [EntityBackup(Encrypt = false)]
    public class PublicContent : Entity<PublicContent> { }
}
```

### Pattern 3: Mixed Security Levels

```csharp
// No assembly-level scope - explicit control
namespace MyApp.Models
{
    // Public entities - no encryption
    [EntityBackup]
    public class BlogPost : Entity<BlogPost> { }

    // Sensitive entities - encrypted
    [EntityBackup(Encrypt = true)]
    public class User : Entity<User> { }

    [EntityBackup(Encrypt = true)]
    public class Payment : Entity<Payment> { }

    // Cache/derived - excluded
    [EntityBackup(Enabled = false, Reason = "Cache, rebuild on restore")]
    public class ViewCache : Entity<ViewCache> { }
}
```

### Pattern 4: Gradual Migration

If you have many entities and want to migrate gradually:

```csharp
// Week 1: Add assembly scope to maintain current behavior
[assembly: EntityBackupScope(Mode = BackupScope.All)]

// Week 2-4: Add policies to sensitive entities
[EntityBackup(Encrypt = true)]
public class User : Entity<User> { }

// Week 5+: Switch to strict mode and add explicit attributes
// [assembly: EntityBackupScope(Mode = BackupScope.None)]
// Then decorate all entities with [EntityBackup]
```

---

## Policy Resolution Rules

Understanding how policies are resolved:

1. **No scope + No attribute** → ⚠️ Warning (not backed up)
2. **`BackupScope.All` + No attribute** → ✅ Included (inherit assembly defaults)
3. **`BackupScope.All` + `[EntityBackup]`** → ✅ Included (entity overrides assembly)
4. **`BackupScope.None` + No attribute** → ⚠️ Warning (not backed up)
5. **`BackupScope.None` + `[EntityBackup]`** → ✅ Included (explicit opt-in)
6. **Any scope + `[EntityBackup(Enabled = false)]`** → ❌ Excluded (with reason)

---

## Verification Checklist

After migration, verify:

- [ ] **No startup warnings** about uncovered entities
- [ ] **Backup works** - Run backup and check manifest
- [ ] **Manifest integrity** - Verify `Status = Completed` and `ItemCount > 0`
- [ ] **Restore works** - Test restore to confirm data recovery
- [ ] **Policy metadata** - Check manifest has `Encrypt` and `IncludeSchema` fields
- [ ] **Health checks** - Verify `/backup/inventory/health` returns Healthy

---

## Diagnostic Tools

### Check Inventory via API

```bash
# Get full inventory
curl http://localhost:5000/backup/inventory

# Get health status
curl http://localhost:5000/backup/inventory/health

# Refresh inventory
curl -X POST http://localhost:5000/backup/inventory/refresh
```

### Enable Health Checks

```csharp
// In Program.cs
services.AddBackupInventoryHealthCheck();
services.AddHealthChecks()
    .AddCheck<BackupInventoryHealthCheck>("backup-inventory");

app.MapHealthChecks("/health");
```

### Programmatic Access

```csharp
// Get cached inventory from startup
var inventory = KoanAutoRegistrar.GetCachedInventory();

Console.WriteLine($"Included: {inventory.TotalIncludedEntities}");
Console.WriteLine($"Warnings: {inventory.TotalWarnings}");

foreach (var warning in inventory.Warnings)
{
    Console.WriteLine($"  {warning}");
}
```

---

## Troubleshooting

### Issue: "Entity X has no backup coverage"

**Cause:** Entity lacks `[EntityBackup]` attribute and assembly has `BackupScope.None` or no scope.

**Solution:**
```csharp
// Add entity-level attribute
[EntityBackup]
public class X : Entity<X> { }

// OR add assembly-level scope
[assembly: EntityBackupScope(Mode = BackupScope.All)]
```

### Issue: "Backup completed with 0 items"

**Cause:** Entity repository not initialized or empty dataset.

**Solution:**
1. Verify entity has data: `await X.All()`
2. Check repository is registered in DI
3. Review backup logs for errors

### Issue: "Backup manifest Status = Failed"

**Cause:** One or more entities threw exceptions during backup.

**Solution:**
1. Check manifest `Entities` list for `ErrorMessage != null`
2. Review application logs for backup exceptions
3. Fix underlying issue (connection, permissions, etc.)
4. Re-run backup

### Issue: "Restore fails with 'manifest indicates failed backup'"

**Cause:** Attempting to restore a failed backup without `AllowPartialRestore = true`.

**Solution:**
```csharp
// Allow partial restore of successful entities
await restoreService.RestoreAllEntitiesAsync(backupName, new GlobalRestoreOptions
{
    AllowPartialRestore = true
});
```

---

## Breaking Changes Summary

| Change | Impact | Migration |
|--------|--------|-----------|
| Auto-discovery removed | Entities not backed up by default | Add `[EntityBackup]` or assembly scope |
| Manifest integrity validation | Failed backups rejected on restore | Fix backup issues or use `AllowPartialRestore` |
| Startup inventory required | Warnings logged if entities uncovered | Address warnings by adding attributes |
| Policy metadata required | Encryption/schema tracked per entity | Attributes populate metadata automatically |

---

## Support

For questions or issues:
- Review [BACKUP-SYSTEM.md](../design/BACKUP-SYSTEM.md) for design details
- Check [ADR-XXXX-koan-backup-reboot.md](../decisions/ADR-XXXX-koan-backup-reboot.md) for technical rationale
- File issues at [GitHub Issues](https://github.com/anthropics/koan-framework/issues)

---

**Last Updated:** 2025-10-03
**Version:** 1.0 (Backup System Reboot - Phase 4)
