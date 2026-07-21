using Koan.Data.Core.Model;
using Koan.Jobs;
using Microsoft.Extensions.DependencyInjection;
using SnapVault.Services;

namespace SnapVault.Models;

/// <summary>
/// Durable studio-scoped photo processing. Two actions share one handler:
/// <list type="bullet">
/// <item><see cref="Ingest"/> — storage, EXIF, organization, optional AI, and embedding from a
/// staged raw blob.</item>
/// <item><see cref="Reanalyze"/> — regenerate AI analysis for an existing photo.</item>
/// </list>
///
/// Koan captures the ambient studio at submission and restores it before execution, so Data, Storage, and Vector
/// operations remain inside the submitting studio without job-specific routing code.
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
    /// <summary>The upload batch represented by this job in the progress projection.</summary>
    public string BatchJobId { get; set; } = "";

    // --- Reanalyze inputs ---
    public string? PhotoId { get; set; }
    public string? AnalysisStyleId { get; set; }

    public static async Task Execute(PhotoProcessingJob job, JobContext ctx, CancellationToken ct)
    {
        var service = ctx.Services.GetRequiredService<PhotoProcessingService>();

        switch (ctx.Action)
        {
            case Ingest:
                // Run the staged upload and persist progress in the Jobs ledger.
                await using (var raw = await UploadStaging.OpenRead(job.StagingKey, ct))
                {
                    var photo = await service.ProcessUpload(
                        job.EventId, raw, job.OriginalFileName, job.ContentType,
                        (fraction, stage) => ctx.Progress(fraction, stage), ct);
                    // Let the progress projection link this work item to its photo.
                    job.PhotoId = photo.Id;
                }

                // Delete staging only after success so retries can reread the original bytes.
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
