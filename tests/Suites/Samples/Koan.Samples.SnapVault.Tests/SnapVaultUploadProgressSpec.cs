using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Koan.Jobs;
using Koan.Tenancy;
using Koan.Web.Sse;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using SnapVault.Models;
using SnapVault.Progress;
using Xunit;

namespace Koan.Samples.SnapVault.Tests;

/// <summary>
/// SnapVault D4 — upload progress as a read-projection of the durable jobs ledger (the SSE replacement for the
/// SignalR hub). A real <c>AddKoan()</c> boot (ARCH-0079, in-memory, no Docker) proves the transport end-to-end
/// against the REAL ledger: a batch of <c>PhotoProcessingJob</c>s is submitted (queued), one is advanced with a
/// real <c>ctx.Progress</c>-equivalent ledger write (stage flows through), then all settle — and the
/// <see cref="UploadProgressProjection"/> reports each photo's state and a single terminal completion. The SSE
/// stream leg proves the wire the browser's <c>EventSource</c> consumes: <c>PhotoProgress</c>/<c>JobCompleted</c>
/// frame names + camelCase field names.
///
/// <para>The <c>processing</c>/<c>completed</c> statuses and dense per-stage emission are driven by the rebuilt
/// ingest pipeline in step 5 (its <c>ctx.Progress</c> calls are the emitter this reads); step 4 owns the read side,
/// so terminal state here is reached deterministically via <c>Cancel</c> rather than by running the (still-legacy,
/// unregistered) ingest service.</para>
/// </summary>
[Collection("snapvault")]
public sealed class SnapVaultUploadProgressSpec
{
    private readonly SnapVaultHostFixture _fx;
    public SnapVaultUploadProgressSpec(SnapVaultHostFixture fx) => _fx = fx;

    private T Svc<T>() where T : notnull => _fx.Host.Services.GetRequiredService<T>();
    private static string Stamp() => Guid.NewGuid().ToString("n").Substring(0, 8);

    private async Task<(string studio, string batch, PhotoProcessingJob[] jobs)> SeedBatchAsync(int count)
    {
        var studio = "studio-" + Stamp();
        var batch = "batch-" + Stamp();
        PhotoProcessingJob[] jobs;
        using (Tenant.Use(studio))
        {
            jobs = Enumerable.Range(0, count)
                .Select(i => new PhotoProcessingJob { BatchJobId = batch, OriginalFileName = $"photo-{i}.jpg" })
                .ToArray();
            await jobs.Submit(PhotoProcessingJob.Ingest);   // saves each work-item + appends a Queued ledger row
        }
        return (studio, batch, jobs);
    }

    [Fact(DisplayName = "batch progress projects from the jobs ledger: queued → staged progress → terminal completion")]
    public async Task Batch_progress_projects_from_the_ledger()
    {
        var ledger = Svc<IJobLedger>();
        var (studio, batch, jobs) = await SeedBatchAsync(3);

        // Queued: three photos, each keyed to the batch, none complete yet — identity comes from the work-item.
        UploadProgressProjection.Snapshot snap;
        using (Tenant.Use(studio)) snap = await UploadProgressProjection.SnapshotAsync(batch);
        snap.IsComplete.Should().BeFalse();
        snap.Completion.Should().BeNull();
        snap.Photos.Select(p => p.FileName).Should().BeEquivalentTo("photo-0.jpg", "photo-1.jpg", "photo-2.jpg");
        snap.Photos.Should().OnlyContain(p => p.Status == "queued" && p.JobId == batch);
        // Every photo carries a distinct, stable work-item id — the key that keeps duplicate filenames apart.
        snap.Photos.Select(p => p.WorkItemId).Should().OnlyHaveUniqueItems().And.NotContain("");

        // Stage flows from the ledger's ProgressMessage — the field the rebuilt pipeline sets via ctx.Progress.
        var rec0 = (await PhotoProcessingJob.Jobs.Query(new JobQuery(WorkId: jobs[0].Id))).Single();
        await ledger.Progress(rec0.Id, 0.5, PhotoProcessingStage.Thumbnails, CancellationToken.None);
        using (Tenant.Use(studio)) snap = await UploadProgressProjection.SnapshotAsync(batch);
        snap.Photos.Single(p => p.FileName == "photo-0.jpg").Stage.Should().Be("thumbnails");
        snap.IsComplete.Should().BeFalse();

        // All settle (deterministic terminal via Cancel) → one completion with the right counts.
        foreach (var j in jobs) await PhotoProcessingJob.Jobs.Cancel(j.Id);
        using (Tenant.Use(studio)) snap = await UploadProgressProjection.SnapshotAsync(batch);
        snap.IsComplete.Should().BeTrue();
        snap.Completion.Should().NotBeNull();
        snap.Completion!.TotalPhotos.Should().Be(3);
        snap.Completion.SuccessCount.Should().Be(0);
        snap.Completion.FailureCount.Should().Be(3);
        snap.Completion.Status.Should().Be("failed");   // all cancelled = zero successes
    }

    [Fact(DisplayName = "SSE stream emits a PhotoProgress frame per photo + a terminal JobCompleted, camelCase on the wire")]
    public async Task Stream_emits_camelCase_frames_then_completion()
    {
        var (studio, batch, jobs) = await SeedBatchAsync(2);
        foreach (var j in jobs) await PhotoProcessingJob.Jobs.Cancel(j.Id);   // terminal → the stream completes on tick 1

        // A timeout token so a regression that breaks completion detection fails fast instead of hanging the runner.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var frames = new List<SseEnvelope>();
        using (Tenant.Use(studio))
            await foreach (var e in UploadProgressProjection.StreamAsync(batch, cts.Token))
                frames.Add(e);

        frames.Count(f => f.EventName == "PhotoProgress").Should().Be(2);
        frames.Should().ContainSingle(f => f.EventName == "JobCompleted");
        // The terminal frame is last (the stream closes after it).
        frames[^1].EventName.Should().Be("JobCompleted");

        // The wire is camelCase — the exact field names the browser reads (incl. the stable workItemId key).
        var progress = JObject.Parse(frames.First(f => f.EventName == "PhotoProgress").Data);
        progress.Properties().Select(p => p.Name).Should().Contain(new[] { "jobId", "workItemId", "fileName", "status", "stage" });
        var completion = JObject.Parse(frames.Single(f => f.EventName == "JobCompleted").Data);
        completion.Properties().Select(p => p.Name).Should().Contain(new[] { "successCount", "failureCount", "totalPhotos" });
    }

    [Fact(DisplayName = "an unknown batch closes the stream immediately (completion, no photo frames) instead of spinning")]
    public async Task Unknown_batch_stream_closes_immediately()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var frames = new List<SseEnvelope>();
        using (Tenant.Use("studio-" + Stamp()))
            await foreach (var e in UploadProgressProjection.StreamAsync("no-such-batch-" + Stamp(), cts.Token))
                frames.Add(e);

        frames.Should().ContainSingle(f => f.EventName == "JobCompleted");
        frames.Should().NotContain(f => f.EventName == "PhotoProgress");
    }
}
