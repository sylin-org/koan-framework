# Koan.Data.Backup

> ✅ Validated against streaming backup/restore pipelines and maintenance loops on **2025-09-29**. See [`TECHNICAL.md`](./TECHNICAL.md) for the full reference.

## Contract

- **Purpose**: Provide backup and restore orchestration for Koan data entities with streaming export, import staging, and progress tracking.
- **Primary inputs**: `BackupPlan` definitions, entity metadata from `Data<TEntity, TKey>`, storage adapters registered through Koan Core.
- **Outputs**: Snapshot archives streamed via `BackupSession`, restore jobs orchestrated through `IDataRestoreService`, and progress metrics surfaced to Web controllers.
- **Failure modes**: Storage adapters lacking backup capability, long-running exports exceeding timeout, or restore pipelines missing entity registrations.
- **Success criteria**: Backups stream without choking memory, progress endpoints report accurate percentages, and restore jobs reconcile entity versions safely.

## Quick start

```csharp
using Koan.Data.Backup.Core;
using Koan.Data.Backup.Models;

public static class BackupPlans
{
    public static BackupPlan OrdersDaily() => new()
    {
        PlanId = "orders:daily",
        Description = "Incremental order backup",
        Entities =
        {
            BackupEntity.For<Order>(bucket: "orders", scope: "tenant-a"),
            BackupEntity.For<Invoice>(bucket: "billing", scope: "tenant-a")
        }
    };
}

public sealed class BackupAutoRegistrar : IKoanAutoRegistrar
{
    public string ModuleName => "Backup";

    public void Initialize(IServiceCollection services)
        => services.AddBackupPlan(BackupPlans.OrdersDaily());

    public void Describe(BootReport report, IConfiguration cfg, IHostEnvironment env)
        => report.AddModule("Backup", "daily");
}
```

- Define backup plans with strong typing and register them through Koan auto-registrar to activate background jobs and HTTP APIs.
- Use `BackupSession.StartAsync(...)` to trigger on-demand exports or `RestoreSession` for imports, leveraging `Data<TEntity, TKey>` statics for persistence.

## Configuration

- Configure storage-specific settings (e.g., bucket, scope, container) via options bound in your adapter.
- For large datasets, enable streaming exports (`AllStream`) and chunked uploads.
- Inject `IBackupDiscoveryService` to query available plans and wiring into UI or CLI surfaces.

## Edge cases

- Paused backups: resume by reinvoking `BackupSession.ResumeAsync` with the stored continuation token.
- Partially restored entities: use `RestoreDiagnostics` to replay failed rows and avoid duplicates.
- Tenant isolation: ensure plan bucket/scope values isolate multi-tenant data to prevent leakage.
- Storage quota: monitor adapter capability responses (`AdapterCapabilities`) to avoid saturating cold storage.

## Related packages

- `Koan.Data.Core` – entity persistence primitives powering backup sessions.
- `Koan.Web.Backup` – HTTP controllers for monitoring and controlling backups.
- `Koan.Core.Adapters` – capability plumbing used by backup storage providers.

## Documentation

- [`TECHNICAL.md`](./TECHNICAL.md) – end-to-end architecture, workflows, configuration, and edge cases.

## Reference

- `IBackupPlanRegistry` – plan discovery and registration.
- `IDataBackupService` – programmatic API for running backups.
- `Data.Backup.Extensions` – helper methods for DI registration.
