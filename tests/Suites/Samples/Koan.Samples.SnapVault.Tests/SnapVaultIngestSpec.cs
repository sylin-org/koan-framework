using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Core;
using Koan.Data.Access;
using Koan.Data.Core;
using Koan.Jobs;
using Koan.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using SnapVault.Models;
using SnapVault.Progress;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Koan.Samples.SnapVault.Tests;

/// <summary>
/// SnapVault step 5a — the greenfield ingest pipeline, end-to-end through the REAL durable job. A real
/// <c>AddKoan()</c> boot (ARCH-0079, in-memory record store + Local blob storage, no Docker) stages a real JPEG,
/// submits a <c>PhotoProcessingJob</c>, drains it, and proves: the pipeline creates the <see cref="PhotoAsset"/>
/// from the staged bytes with EXIF-derived dimensions, auto-organizes it into a daily <see cref="Event"/>, stamps
/// the photo id back onto the work-item, and reports progress to the ledger so the step-4 SSE projection reports
/// the batch complete. (No AI provider in a unit run — the pipeline swallows the vision failure as non-fatal, so
/// the job still completes: storage + EXIF + daily-event succeed.)
/// </summary>
[Collection("snapvault")]
public sealed class SnapVaultIngestSpec
{
    private readonly SnapVaultHostFixture _fx;
    public SnapVaultIngestSpec(SnapVaultHostFixture fx) => _fx = fx;

    private Task Drain(CancellationToken ct = default)
        => _fx.Host.Services.GetRequiredService<JobOrchestrator>().DrainAsync(ct);

    private static string Stamp() => Guid.NewGuid().ToString("n").Substring(0, 8);

    private static async Task<byte[]> TinyJpegAsync()
    {
        using var img = new Image<Rgba32>(8, 6);   // 8x6 so width != height proves the dimensions are read, not guessed
        using var ms = new MemoryStream();
        await img.SaveAsJpegAsync(ms);
        return ms.ToArray();
    }

    [Fact(DisplayName = "ingest: a submitted PhotoProcessingJob stages → creates the photo (dims + daily event) → completes, progress on the ledger")]
    public async Task Ingest_pipeline_creates_the_photo_and_completes()
    {
        var studio = "studio-" + Stamp();
        var batch = "batch-" + Stamp();
        var jpeg = await TinyJpegAsync();

        PhotoProcessingJob job;
        using (Tenant.Use(studio))
        {
            var staged = await UploadStaging.Onboard($"{Stamp()}.jpg", new MemoryStream(jpeg), "image/jpeg");
            job = new PhotoProcessingJob
            {
                EventId = null,                    // auto-organize into a daily event
                OriginalFileName = "sunset.jpg",
                ContentType = "image/jpeg",
                StagingKey = staged.Key,
                BatchJobId = batch,
            };
            await job.Job.Submit(PhotoProcessingJob.Ingest);
        }

        // Drain to terminal (Ingest has MaxAttempts=3; a real completion settles on the first pass).
        for (var i = 0; i < 5; i++)
        {
            await Drain();
            var status = await PhotoProcessingJob.Jobs.Status(job.Id);
            if (status is JobStatus.Completed or JobStatus.Failed or JobStatus.Dead or JobStatus.Cancelled) break;
        }

        (await PhotoProcessingJob.Jobs.Status(job.Id)).Should().Be(JobStatus.Completed);

        // The pipeline created the photo under the studio, with dimensions read from the JPEG and a daily event.
        using (Tenant.Use(studio))
        using (Subject.System())
        {
            var photos = await PhotoAsset.All();
            var photo = photos.Single(p => p.OriginalFileName == "sunset.jpg");
            photo.Width.Should().Be(8);
            photo.Height.Should().Be(6);
            photo.EventId.Should().NotBeNullOrEmpty();

            var evt = await Event.Get(photo.EventId, CancellationToken.None);
            evt.Should().NotBeNull();
            evt!.Type.Should().Be(EventType.DailyAuto);

            // The work-item carries the created photo id back (save-if-changed), so progress can surface it.
            var reloaded = await PhotoProcessingJob.Get(job.Id, CancellationToken.None);
            reloaded!.PhotoId.Should().Be(photo.Id);
        }

        // The step-4 SSE projection sees the batch complete (closes the loop between ingest and progress).
        using (Tenant.Use(studio))
        {
            var snap = await UploadProgressProjection.SnapshotAsync(batch);
            snap.IsComplete.Should().BeTrue();
            snap.Completion!.SuccessCount.Should().Be(1);
            snap.Completion.FailureCount.Should().Be(0);
        }
    }
}
