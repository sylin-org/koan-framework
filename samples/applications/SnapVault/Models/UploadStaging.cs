using Koan.Storage.Infrastructure;
using Koan.Storage.Model;

namespace SnapVault.Models;

/// <summary>
/// Transient, crash-safe staging for a raw uploaded file. The upload endpoint streams the bytes here and
/// returns immediately; the durable <see cref="PhotoProcessingJob"/> reads them back when it runs.
///
/// Why stage instead of carrying the bytes on the job? The job work-item is persisted in the job ledger —
/// megabytes of image data must never land there. Staging to storage is also what a real pipeline does: the
/// HTTP request returns fast and the bytes survive a process restart before processing completes.
///
/// Tenant isolation is automatic: the data row is scoped by the ambient tenant and the blob key is prefixed
/// with the tenant by the ScopedStorageService decorator (SnapVault Phase 1 / GAP C 0.4). The job runs in the
/// submitter's rehydrated tenant (ARCH-0100), so it reads back its own studio's staged blob and no other's.
/// </summary>
[StorageBinding(Profile = "cold", Container = "staging")]
public sealed class UploadStaging : StorageEntity<UploadStaging>
{
}
