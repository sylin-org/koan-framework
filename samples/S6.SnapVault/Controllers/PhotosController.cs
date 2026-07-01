using Koan.Jobs;
using Microsoft.AspNetCore.Mvc;
using S6.SnapVault.Models;

namespace S6.SnapVault.Controllers;

/// <summary>
/// The studio photo surface (SnapVault greenfield). Step 5a ships the upload action (#8); the read/query and
/// mutation actions (#1–#7, #9–#19) are appended in steps 5b/5c. Isolation is inherited from the ambient tenancy
/// posture — reads/writes go through the data layer, so a studio operator's requests are studio-scoped and a
/// guest's would be event-scoped (SEC-0008), with no per-endpoint auth ceremony (see UploadProgressController).
/// </summary>
[ApiController]
[Route("api/photos")]
public sealed class PhotosController : ControllerBase
{
    private const long MaxFileBytes = 25L * 1024 * 1024;                 // 25 MB per file
    private static readonly HashSet<string> AllowedExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".heic", ".heif" };

    /// <summary>
    /// #8 — Batch upload. Streams each raw file to crash-safe staging (never onto the job ledger), then submits one
    /// durable, tenant-carried <see cref="PhotoProcessingJob"/> per file sharing a <c>batchId</c>. Returns immediately
    /// with that batch id; the browser opens the SSE progress stream on it. The ambient studio tenant is captured at
    /// submit and rehydrated when each job runs (ARCH-0100).
    /// </summary>
    [HttpPost("upload")]
    [RequestSizeLimit(260L * 1024 * 1024)]                                  // UI max batch = 10 files × 25 MB + headroom
    [RequestFormLimits(MultipartBodyLengthLimit = 260L * 1024 * 1024)]
    public async Task<IActionResult> Upload(CancellationToken ct)
    {
        var form = await Request.ReadFormAsync(ct);
        if (form.Files.Count == 0)
            return BadRequest(new { error = "No files provided." });

        // "auto" (or omitted) means auto-organize by EXIF capture date; the pipeline mints the daily event.
        var eventIdRaw = form["eventId"].ToString();
        var eventId = string.IsNullOrEmpty(eventIdRaw) || eventIdRaw == "auto" ? null : eventIdRaw;

        var batchId = Guid.NewGuid().ToString("n");   // unguessable per-upload batch key (the SSE stream subscribes to it)
        var jobs = new List<PhotoProcessingJob>();
        var failed = new List<string>();

        foreach (var file in form.Files)
        {
            var ext = Path.GetExtension(file.FileName);
            if (string.IsNullOrEmpty(ext) || !AllowedExtensions.Contains(ext) || file.Length <= 0 || file.Length > MaxFileBytes)
            {
                failed.Add(file.FileName);
                continue;
            }

            try
            {
                // Stage the raw bytes (tenant-prefixed, crash-safe) and enqueue the job that reads them back.
                await using var stream = file.OpenReadStream();
                var contentType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType;
                var staged = await UploadStaging.Onboard(file.FileName, stream, contentType);
                jobs.Add(new PhotoProcessingJob
                {
                    EventId = eventId,
                    OriginalFileName = file.FileName,
                    ContentType = contentType,
                    StagingKey = staged.Key,
                    BatchJobId = batchId,
                });
            }
            catch (Exception)
            {
                failed.Add(file.FileName);
            }
        }

        if (jobs.Count > 0)
            await jobs.Submit(PhotoProcessingJob.Ingest, ct);

        // Shape honored by upload.js: { jobId, totalQueued } (totalFailed is additive).
        return Ok(new { jobId = batchId, totalQueued = jobs.Count, totalFailed = failed.Count });
    }
}
