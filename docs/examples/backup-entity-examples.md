# Backup Entity Examples

**Purpose:** Demonstrates various `[EntityBackup]` and `[EntityBackupScope]` attribute patterns for different use cases.

---

## Basic Examples

### Example 1: Simple Opt-In

```csharp
using Koan.Data.Abstractions;
using Koan.Data.Backup.Attributes;

namespace MyApp.Models;

/// <summary>
/// Basic entity with backup enabled using default policies.
/// </summary>
[EntityBackup]
public class BlogPost : Entity<BlogPost>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTimeOffset PublishedAt { get; set; }
}
```

**Result:**
- ✅ Backed up during full backup operations
- 🔓 Not encrypted (default)
- 📋 Schema included in backup (default)

---

### Example 2: PII with Encryption

```csharp
/// <summary>
/// User entity containing PII - encryption enabled for compliance.
/// </summary>
[EntityBackup(Encrypt = true)]
public class User : Entity<User>
{
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}
```

**Result:**
- ✅ Backed up with encryption
- 🔐 Backup data encrypted at rest
- 📋 Schema included in backup
- 🔄 Restore automatically decrypts data

---

### Example 3: High-Volume Logs (No Schema)

```csharp
/// <summary>
/// Log entries entity - schema excluded to reduce backup size.
/// Restore uses current schema instead of backed-up schema.
/// </summary>
[EntityBackup(IncludeSchema = false)]
public class LogEntry : Entity<LogEntry>
{
    public string Message { get; set; } = "";
    public string Level { get; set; } = "Info";
    public DateTimeOffset Timestamp { get; set; }
    public Dictionary<string, object> Properties { get; set; } = new();
}
```

**Result:**
- ✅ Backed up without schema snapshot
- 💾 Smaller backup files (no schema overhead)
- ⚠️ Restore assumes current schema matches backup data

---

### Example 4: Explicit Opt-Out

```csharp
/// <summary>
/// Search index - excluded from backups because it can be rebuilt.
/// </summary>
[EntityBackup(Enabled = false, Reason = "Derived from BlogPost, rebuild on restore")]
public class SearchIndex : Entity<SearchIndex>
{
    public string IndexData { get; set; } = "";
    public DateTimeOffset LastUpdated { get; set; }
}
```

**Result:**
- ❌ Excluded from backups
- 📝 Reason documented in inventory
- ✅ No startup warning (reason provided)

---

## Assembly-Level Scope Examples

### Example 5: Opt-In All Entities

```csharp
using Koan.Data.Backup.Attributes;

// Assembly-level attribute - applies to all entities in this assembly
[assembly: EntityBackupScope(Mode = BackupScope.All)]

namespace MyApp.Models;

// Automatically included (inherits assembly scope)
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
}

// Automatically included (inherits assembly scope)
public class Category : Entity<Category>
{
    public string Name { get; set; } = "";
}

// Can override assembly defaults
[EntityBackup(Encrypt = true)]
public class Payment : Entity<Payment>
{
    public decimal Amount { get; set; }
    public string CardLastFour { get; set; } = "";
}
```

**Result:**
- ✅ Product: Backed up (via assembly scope)
- ✅ Category: Backed up (via assembly scope)
- ✅ Payment: Backed up with encryption (override)

---

### Example 6: Encrypt All by Default

```csharp
using Koan.Data.Backup.Attributes;

// Encrypt all entities in this assembly by default
[assembly: EntityBackupScope(Mode = BackupScope.All, EncryptByDefault = true)]

namespace MyApp.SecureModels;

// Uses assembly default (encrypted)
public class CustomerData : Entity<CustomerData>
{
    public string Name { get; set; } = "";
    public string SSN { get; set; } = "";
}

// Override for public data
[EntityBackup(Encrypt = false)]
public class PublicAnnouncement : Entity<PublicAnnouncement>
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}
```

**Result:**
- ✅ CustomerData: Backed up with encryption (via EncryptByDefault)
- ✅ PublicAnnouncement: Backed up without encryption (override)

---

### Example 7: Strict Mode (Require Explicit Attributes)

```csharp
using Koan.Data.Backup.Attributes;

// Require explicit [EntityBackup] on every entity
[assembly: EntityBackupScope(Mode = BackupScope.None)]

namespace MyApp.CriticalData;

// Explicitly opted in
[EntityBackup(Encrypt = true)]
public class SensitiveDocument : Entity<SensitiveDocument>
{
    public string Content { get; set; } = "";
}

// ⚠️ Generates startup warning - no backup coverage
public class UnmarkedEntity : Entity<UnmarkedEntity>
{
    public string Data { get; set; } = "";
}
```

**Result:**
- ✅ SensitiveDocument: Backed up with encryption
- ⚠️ UnmarkedEntity: Warning logged at startup, not backed up

---

## Advanced Examples

### Example 8: Multi-Tenant Application

```csharp
[assembly: EntityBackupScope(Mode = BackupScope.All, EncryptByDefault = true)]

namespace MultiTenantApp.Models;

// Tenant data - encrypted by default from assembly
public class TenantData : Entity<TenantData>
{
    public Guid TenantId { get; set; }
    public string CompanyName { get; set; } = "";
    public Dictionary<string, object> Settings { get; set; } = new();
}

// Tenant users - encrypted
public class TenantUser : Entity<TenantUser>
{
    public Guid TenantId { get; set; }
    public string Email { get; set; } = "";
}

// Shared configuration - not encrypted (override)
[EntityBackup(Encrypt = false)]
public class SharedTemplate : Entity<SharedTemplate>
{
    public string Name { get; set; } = "";
    public string Content { get; set; } = "";
}
```

---

### Example 9: E-Commerce Platform

```csharp
namespace ECommerce.Models;

// Products - public data, no encryption needed
[EntityBackup]
public class Product : Entity<Product>
{
    public string Name { get; set; } = "";
    public decimal Price { get; set; }
    public string[] ImageUrls { get; set; } = Array.Empty<string>();
}

// Orders - contains PII, encrypt
[EntityBackup(Encrypt = true)]
public class Order : Entity<Order>
{
    public Guid UserId { get; set; }
    public decimal Total { get; set; }
    public string ShippingAddress { get; set; } = "";
    public string BillingAddress { get; set; } = "";
}

// Cart - temporary data, opt-out
[EntityBackup(Enabled = false, Reason = "Temporary shopping cart, no backup needed")]
public class ShoppingCart : Entity<ShoppingCart>
{
    public Guid UserId { get; set; }
    public List<Guid> ProductIds { get; set; } = new();
}

// Analytics - high volume, no schema
[EntityBackup(IncludeSchema = false)]
public class ProductView : Entity<ProductView>
{
    public Guid ProductId { get; set; }
    public Guid? UserId { get; set; }
    public DateTimeOffset ViewedAt { get; set; }
    public string UserAgent { get; set; } = "";
}
```

---

### Example 10: Healthcare Application (HIPAA Compliance)

```csharp
using Koan.Data.Backup.Attributes;

// All healthcare data must be encrypted
[assembly: EntityBackupScope(Mode = BackupScope.None)]

namespace HealthApp.Models;

// Patient records - explicit opt-in, encrypted
[EntityBackup(Encrypt = true)]
public class PatientRecord : Entity<PatientRecord>
{
    public string PatientId { get; set; } = "";
    public string MedicalHistory { get; set; } = "";
    public DateTimeOffset LastVisit { get; set; }
}

// Appointments - encrypted
[EntityBackup(Encrypt = true)]
public class Appointment : Entity<Appointment>
{
    public Guid PatientId { get; set; }
    public Guid ProviderId { get; set; }
    public DateTimeOffset ScheduledTime { get; set; }
    public string Notes { get; set; } = "";
}

// Audit logs - encrypted, no schema (high volume)
[EntityBackup(Encrypt = true, IncludeSchema = false)]
public class AuditLog : Entity<AuditLog>
{
    public string Action { get; set; } = "";
    public Guid UserId { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
}
```

---

## Policy Summary Table

| Attribute | Encrypt | IncludeSchema | In Backup | Use Case |
|-----------|---------|---------------|-----------|----------|
| `[EntityBackup]` | false | true | ✅ | Public data, default behavior |
| `[EntityBackup(Encrypt = true)]` | true | true | ✅ | PII, sensitive data |
| `[EntityBackup(IncludeSchema = false)]` | false | false | ✅ | High-volume logs |
| `[EntityBackup(Enabled = false, Reason = "...")]` | n/a | n/a | ❌ | Cache, derived data |
| Assembly `BackupScope.All` | false | true | ✅ | Opt-in all entities |
| Assembly `BackupScope.All, EncryptByDefault = true` | true | true | ✅ | Secure/multi-tenant apps |
| Assembly `BackupScope.None` | n/a | n/a | ⚠️ | Strict mode, require explicit |

---

## Best Practices

1. **Default to assembly scope** for most projects: `[assembly: EntityBackupScope(Mode = BackupScope.All)]`
2. **Use strict mode** (`BackupScope.None`) for security-critical applications
3. **Always provide `Reason`** when setting `Enabled = false`
4. **Enable encryption** for PII, credentials, financial data
5. **Exclude schema** for high-volume, stable entities (logs, analytics)
6. **Validate inventory** at startup to catch missing coverage

---

**Last Updated:** 2025-10-03
**Related:** [BACKUP-MIGRATION-GUIDE.md](../guides/BACKUP-MIGRATION-GUIDE.md), [BACKUP-SYSTEM.md](../design/BACKUP-SYSTEM.md)
