using Koan.Data.Core.Model;
using Koan.Jobs;
using Microsoft.Extensions.DependencyInjection;
using S6.SnapVault.Services;

namespace S6.SnapVault.Models;

/// <summary>
/// Durable, tenant-carrying background work for photo processing — the Koan.Jobs replacement for the old
/// in-memory <c>IPhotoProcessingQueue</c> + <c>PhotoProcessingWorker</c> (SnapVault Phase 1 break-and-rebuild).
///
/// Two actions share one handler:
/// <list type="bullet">
/// <item><see cref="Ingest"/> — the full upload pipeline (storage, derivatives, EXIF, AI, embedding) from a
/// staged raw blob.</item>
/// <item><see cref="Reanalyze"/> — regenerate AI analysis for an existing photo.</item>
/// </list>
///
/// The ambient tenant is captured at <c>Submit</c> and rehydrated before <c>Execute</c> runs (ARCH-0100
/// durable carrier) — so every entity read/write, blob open, and vector upsert the handler performs happens in
/// the studio that submitted the work. The author writes no tenant code; it rides for free.
/// </summary>
[JobAction(PhotoProcessingJob.Ingest, Timeout = "00:15:00", MaxAttempts = 3)]
[JobAction(PhotoProcessingJob.Reanalyze, Timeout = "00:10:00", MaxAttempts = 3)]
public sealed class PhotoProcessingJob : Entity<PhotoProcessingJob>, IKoanJob<PhotoProcessingJob>
{
    public const string Ingest = nameof(Ingest);
    public const string Reanalyze = nameof(Reanalyze);

    // --- Ingest inputs ---
    /// <summary>Target event id, or null to auto-assign a daily album from EXIF capture date.</summary>
    public string? EventId { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    /// <summary>Storage key of the staged raw upload (see <see cref="UploadStaging"/>).</summary>
    public string StagingKey { get; set; } = "";
    /// <summary>The batch-progress id for progress/error aggregation (legacy batch tracker, dropped in step 1).</summary>
    public string BatchJobId { get; set; } = "";

    // --- Reanalyze inputs ---
    public string? PhotoId { get; set; }
    public string? AnalysisStyleId { get; set; }

    public static async Task Execute(PhotoProcessingJob job, JobContext ctx, CancellationToken ct)
    {
        var service = ctx.Services.GetRequiredService<IPhotoProcessingService>();

        switch (ctx.Action)
        {
            case Ingest:
                // Open the staged raw blob (resolved under this job's rehydrated tenant) and run the pipeline.
                await using (var raw = await UploadStaging.OpenRead(job.StagingKey, ct))
                {
                    await service.ProcessUpload(job.EventId, raw, job.OriginalFileName, job.ContentType, job.BatchJobId, ct);
                }

                // Success → drop the transient staging blob. A retryable failure above leaves it in place so the
                // next attempt can re-read it (delete-on-success, not in a finally). NOTE: a job that exhausts its
                // retries (dead-lettered) leaves its staging blob behind — reclaimable, not corrupting; a periodic
                // TTL sweep of the staging container is the production follow-on (out of Phase-1 scope).
                try { await UploadStaging.Get(job.StagingKey).Delete(ct); }
                catch { /* best-effort cleanup */ }
                break;

            case Reanalyze:
                if (!string.IsNullOrEmpty(job.PhotoId))
                    await service.RegenerateAIAnalysis(job.PhotoId, job.AnalysisStyleId, ct);
                break;
        }
    }
}
