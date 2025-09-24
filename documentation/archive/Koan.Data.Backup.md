# Koan.Data.Backup - Complete Data Backup & Restore

The Koan.Data.Backup module provides comprehensive backup and restore capabilities for Koan Framework applications. It automatically discovers all Entity<> types across your data providers and enables seamless data archival with enterprise-grade features.

## Features

- **Zero-Configuration Operation** - Automatically discovers all Entity<> types
- **Multi-Provider Support** - Works across SQL, NoSQL, Vector, and JSON stores
- **Progressive Complexity** - From one-line operations to enterprise scenarios
- **Streaming Architecture** - Memory-efficient processing of large datasets
- **ZIP + JSON Lines Format** - Simple, inspectable, and compressed archives
- **Verification & Validation** - Built-in integrity checking and schema snapshots
- **Koan-Native Integration** - Leverages existing data infrastructure

## Installation

Add the backup module to your project:

```xml
<PackageReference Include="Koan.Data.Backup" Version="1.0.0" />
```

**Zero-Configuration Setup**: The backup services are automatically registered when you call `services.AddKoan()`. No additional service registration is required!

```csharp
services.AddKoan(); // Backup services auto-registered via KoanAutoRegistrar
```

**Optional Manual Configuration** (for advanced scenarios):

```csharp
services.AddKoan();
services.AddKoanBackupRestore(options =>
{
    options.DefaultStorageProfile = "backup-storage";
    options.DefaultBatchSize = 2000;
    options.WarmupEntitiesOnStartup = true;
});
```

## Usage Patterns

### 🚀 Simple: One-Line Operations

The simplest usage requires just one line of code:

```csharp
// Backup all users
await DataBackup.BackupTo<User, Guid>("daily-users");

// Restore all users
await DataBackup.RestoreFrom<User, Guid>("daily-users");

// List available backups
var backups = await DataBackup.ListBackups<User, Guid>();
```

**Perfect for:**
- Development and testing
- Simple backup scripts
- Single entity type scenarios

### 🎯 Targeted: Entity-Specific Operations

Control exactly what gets backed up:

```csharp
// Backup with description
await DataBackup.BackupTo<Product, string>(
    "products-before-migration",
    "Products backup before price migration");

// Backup with full options
var options = new BackupOptions
{
    Description = "Monthly product archive",
    Tags = new[] { "monthly", "products", "archive" },
    StorageProfile = "long-term-storage",
    BatchSize = 1000
};
await DataBackup.BackupTo<Product, string>("monthly-products", options);

// Restore with options
var restoreOptions = new RestoreOptions
{
    ReplaceExisting = true,
    StorageProfile = "fast-restore",
    ValidateBeforeRestore = true
};
await DataBackup.RestoreFrom<Product, string>("monthly-products", restoreOptions);
```

**Perfect for:**
- Specific entity migrations
- Scheduled archival
- Targeted data movements

### 🏢 Enterprise: Full Application Backups

Comprehensive backup and restore operations:

```csharp
// Get the backup service
var backupService = serviceProvider.GetRequiredService<IBackupService>();

// Full application backup
var globalOptions = new GlobalBackupOptions
{
    Description = "Complete application backup before major release",
    Tags = new[] { "release", "v2.0", "full-backup" },
    StorageProfile = "disaster-recovery",
    MaxConcurrency = 4,
    BatchSize = 5000,
    IncludeProviders = new[] { "sqlite", "mongo", "postgres" }, // Optional filter
    ExcludeEntityTypes = new[] { "AuditLog", "TemporaryCache" }  // Skip certain entities
};

var manifest = await backupService.BackupAllEntitiesAsync(
    "release-v2.0-backup",
    globalOptions);

// Inspect what was backed up
Console.WriteLine($"Backup completed: {manifest.Name}");
Console.WriteLine($"Entities: {manifest.Entities.Count}");
Console.WriteLine($"Total items: {manifest.Verification.TotalItemCount:N0}");
Console.WriteLine($"Total size: {manifest.Verification.TotalSizeBytes / (1024*1024):F1} MB");

foreach (var entity in manifest.Entities)
{
    Console.WriteLine($"  {entity.EntityType}: {entity.ItemCount:N0} items ({entity.Provider})");
}

// Full application restore
var restoreService = serviceProvider.GetRequiredService<IRestoreService>();

var globalRestoreOptions = new GlobalRestoreOptions
{
    ValidateBeforeRestore = true,
    ReplaceExisting = false, // Merge with existing data
    MaxConcurrency = 2,
    BatchSize = 2500,
    StorageProfile = "disaster-recovery"
};

await restoreService.RestoreAllEntitiesAsync("release-v2.0-backup", globalRestoreOptions);
```

**Perfect for:**
- Disaster recovery scenarios
- Environment migrations
- Complete system backups
- Compliance requirements

### 🔍 Advanced: Discovery & Management

Query and manage your backup ecosystem:

```csharp
var discoveryService = serviceProvider.GetRequiredService<IBackupDiscoveryService>();

// Discover all available backups
var catalog = await discoveryService.DiscoverAllBackupsAsync();
Console.WriteLine($"Found {catalog.TotalCount} backups across storage profiles");

// Advanced querying
var query = new BackupQuery
{
    Tags = new[] { "daily" },
    EntityTypes = new[] { "User", "Product" },
    DateFrom = DateTimeOffset.UtcNow.AddDays(-30),
    StorageProfiles = new[] { "primary-backup" },
    Statuses = new[] { BackupStatus.Completed },
    SortBy = "CreatedAt",
    SortDirection = SortDirection.Descending,
    Take = 10
};

var results = await discoveryService.QueryBackupsAsync(query);
foreach (var backup in results.Backups)
{
    Console.WriteLine($"{backup.Name} - {backup.CreatedAt:yyyy-MM-dd} - {backup.SizeBytes / 1024:N0} KB");
}

// Validate backup integrity
var validationResult = await discoveryService.ValidateBackupAsync("critical-backup");
if (!validationResult.IsValid)
{
    Console.WriteLine("Backup validation failed:");
    foreach (var issue in validationResult.Issues)
    {
        Console.WriteLine($"  - {issue}");
    }
}

// Get catalog statistics
var stats = await discoveryService.GetCatalogStatsAsync();
Console.WriteLine($"Total backups: {stats.TotalBackups}");
Console.WriteLine($"Total size: {stats.TotalSizeBytes / (1024*1024*1024):F2} GB");
Console.WriteLine($"Healthy backups: {stats.HealthyBackups}");
Console.WriteLine($"Backups needing attention: {stats.BackupsRequiringAttention}");
```

**Perfect for:**
- Backup monitoring dashboards
- Automated validation systems
- Compliance reporting
- Storage optimization

## Configuration Options

### BackupOptions

```csharp
public class BackupOptions
{
    public string? Description { get; set; }
    public string[] Tags { get; set; } = Array.Empty<string>();
    public string StorageProfile { get; set; } = string.Empty;
    public int BatchSize { get; set; } = 1000;
    public bool CompressData { get; set; } = true;
    public bool IncludeSchemaSnapshot { get; set; } = true;
}
```

### GlobalBackupOptions

```csharp
public class GlobalBackupOptions : BackupOptions
{
    public string[]? IncludeProviders { get; set; }
    public string[]? ExcludeProviders { get; set; }
    public string[]? IncludeEntityTypes { get; set; }
    public string[]? ExcludeEntityTypes { get; set; }
    public int MaxConcurrency { get; set; } = 1;
    public bool ContinueOnError { get; set; } = true;
}
```

### RestoreOptions

```csharp
public class RestoreOptions
{
    public string? TargetSet { get; set; }
    public string StorageProfile { get; set; } = string.Empty;
    public bool ReplaceExisting { get; set; } = false;
    public bool ValidateBeforeRestore { get; set; } = true;
    public bool OptimizeForBulkInsert { get; set; } = true;
    public int BatchSize { get; set; } = 1000;
}
```

## Storage Integration

The backup system integrates with Koan.Storage for flexible storage backends:

```csharp
// Configure storage profiles in appsettings.json
{
  "Koan": {
    "Storage": {
      "Profiles": {
        "primary-backup": {
          "Provider": "s3",
          "Settings": {
            "BucketName": "app-backups",
            "Region": "us-west-2"
          }
        },
        "disaster-recovery": {
          "Provider": "azure-blob",
          "Settings": {
            "ConnectionString": "...",
            "ContainerName": "disaster-recovery"
          }
        }
      }
    }
  }
}
```

## Background Services

Enable automatic backup maintenance:

```csharp
services.AddKoanBackupRestore(options =>
{
    options.DefaultStorageProfile = "primary-backup";
    options.DefaultBatchSize = 2000;
    options.WarmupEntitiesOnStartup = true;
    options.EnableBackgroundMaintenance = true;
    options.MaintenanceInterval = TimeSpan.FromHours(6);
    options.RetentionPolicy = new BackupRetentionPolicy
    {
        KeepDaily = 30,
        KeepWeekly = 12,
        KeepMonthly = 24
    };
});
```

## Archive Format

Backup archives use a standard ZIP format with JSON Lines for entity data:

```
backup-name-20241217-143022.zip
├── manifest.json                 # Backup metadata and entity list
├── entities/
│   ├── User.jsonl               # User entities (one JSON object per line)
│   ├── Product.jsonl            # Product entities
│   └── Order.jsonl              # Order entities
└── verification/
    ├── checksums.json           # File integrity checksums
    └── schema-snapshots.json    # Entity schema information
```

Each `.jsonl` file contains one JSON object per line:
```jsonl
{"Id":"01234567-89ab-cdef-0123-456789abcdef","Name":"John Doe","Email":"john@example.com"}
{"Id":"01234567-89ab-cdef-0123-456789abcdef","Name":"Jane Smith","Email":"jane@example.com"}
```

## Error Handling

The backup system provides comprehensive error handling:

```csharp
try
{
    var manifest = await DataBackup.BackupTo<User, Guid>("critical-backup");

    if (manifest.Status == BackupStatus.Failed)
    {
        logger.LogError("Backup failed: {Error}", manifest.ErrorMessage);
        return;
    }

    // Verify backup integrity
    var validation = await discoveryService.ValidateBackupAsync(manifest.Id);
    if (!validation.IsValid)
    {
        logger.LogWarning("Backup completed but validation failed");
        foreach (var issue in validation.Issues)
        {
            logger.LogWarning("Validation issue: {Issue}", issue);
        }
    }
}
catch (BackupException ex)
{
    logger.LogError(ex, "Backup operation failed");
    // Handle backup-specific errors
}
catch (StorageException ex)
{
    logger.LogError(ex, "Storage operation failed");
    // Handle storage-related errors
}
```

## Best Practices

### 1. **Choose the Right Approach**
- Use `DataBackup.BackupTo<>()` for single entity types
- Use `IBackupService.BackupAllEntitiesAsync()` for comprehensive backups
- Use discovery services for backup management and monitoring

### 2. **Storage Strategy**
- Use different storage profiles for different backup types
- Configure long-term storage for compliance backups
- Use fast storage for operational backups

### 3. **Performance Optimization**
- Adjust `BatchSize` based on entity size and available memory
- Use `MaxConcurrency = 1` for very large entities to avoid memory pressure
- Enable `OptimizeForBulkInsert` for faster restores

### 4. **Validation & Monitoring**
- Always validate critical backups after creation
- Set up monitoring for backup health status
- Implement automated validation for compliance requirements

### 5. **Naming Conventions**
```csharp
// Good naming patterns
"daily-users-2024-12-17"           // Date-based
"pre-migration-products"           // Event-based
"disaster-recovery-full"           // Purpose-based
"release-v2.1-complete"            // Version-based
```

## Integration Examples

### ASP.NET Core Controller

```csharp
[ApiController]
[Route("api/[controller]")]
public class BackupController : ControllerBase
{
    private readonly IBackupService _backupService;

    public BackupController(IBackupService backupService)
    {
        _backupService = backupService;
    }

    [HttpPost("create/{name}")]
    public async Task<ActionResult<BackupManifest>> CreateBackup(
        string name,
        [FromBody] GlobalBackupOptions options)
    {
        var manifest = await _backupService.BackupAllEntitiesAsync(name, options);
        return Ok(manifest);
    }

    [HttpGet("list")]
    public async Task<ActionResult<BackupInfo[]>> ListBackups()
    {
        var catalog = await _discoveryService.DiscoverAllBackupsAsync();
        return Ok(catalog.Backups);
    }
}
```

### Background Service

```csharp
public class DailyBackupService : BackgroundService
{
    private readonly IBackupService _backupService;
    private readonly ILogger<DailyBackupService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var backupName = $"daily-backup-{DateTime.UtcNow:yyyy-MM-dd}";
                var options = new GlobalBackupOptions
                {
                    Description = "Automated daily backup",
                    Tags = new[] { "daily", "automated" },
                    StorageProfile = "daily-backup"
                };

                var manifest = await _backupService.BackupAllEntitiesAsync(backupName, options);
                _logger.LogInformation("Daily backup completed: {BackupName}", manifest.Name);

                // Wait 24 hours
                await Task.Delay(TimeSpan.FromDays(1), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Daily backup failed");
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken); // Retry in 1 hour
            }
        }
    }
}
```

## Conclusion

Koan.Data.Backup provides a complete, production-ready backup and restore solution that grows with your application complexity. From simple one-line operations to enterprise-grade disaster recovery systems, it seamlessly integrates with your existing Koan Framework data architecture.

The progressive complexity model ensures you can start simple and add sophistication as your requirements evolve, while the zero-configuration approach means your backups work across all your data providers without additional setup.