using System.Runtime.CompilerServices;
using Koan.Jobs;
using Koan.Web.Sse;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using SnapVault.Models;

namespace SnapVault.Progress;

/// <summary>
/// Upload progress as a read-projection of the durable jobs ledger — the SSE replacement for the SignalR hub.
///
/// <para>The batch is not a stored entity: it is "the <see cref="PhotoProcessingJob"/> work-items sharing a
/// <c>BatchJobId</c>". <see cref="SnapshotAsync"/> resolves that set (tenant-scoped, for free) and joins each to
/// its ledger <see cref="JobRecord"/> — the work-item carries identity (file name, photo id), the ledger carries
/// lifecycle + <c>ctx.Progress</c> (status, stage). No hub, no groups, no push, no separate batch tracker: the
/// ledger is the single source of truth and this is a pure read of it.</para>
///
/// <para><see cref="StreamAsync"/> wraps the snapshot in a poll-diff loop that yields one <see cref="SseEnvelope"/>
/// per changed photo and a terminal completion frame when every job has settled — the browser holds one native
/// <c>EventSource</c> and the loop closes it. The snapshot is split out (pure, host-testable) from the streaming
/// glue on purpose: the meaningful logic — batch→events + completion detection — is asserted directly, without SSE
/// plumbing or timing.</para>
/// </summary>
public static class UploadProgressProjection
{
    /// <summary>How often the stream re-reads the ledger. Server-side only — the browser holds one open connection.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    /// <summary>Safety cap on a single stream's lifetime: jobs always reach a terminal state (the reaper reclaims a
    /// stuck lease), so the loop terminates naturally — this only bounds a pathological never-settling batch so one
    /// connection can't be pinned open forever. The client may reopen.</summary>
    public static readonly TimeSpan MaxStreamDuration = TimeSpan.FromMinutes(30);

    // Field names the browser reads (camelCase); nulls dropped so an absent Error/Stage doesn't appear on the wire.
    private static readonly JsonSerializerSettings Wire = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
        NullValueHandling = NullValueHandling.Ignore,
    };

    /// <summary>One tick: the current per-photo progress for a batch and, once all jobs are terminal, the completion.</summary>
    public sealed record Snapshot(
        IReadOnlyList<PhotoProgressEvent> Photos,
        bool IsComplete,
        JobCompletionEvent? Completion);

    /// <summary>
    /// Resolve the batch's work-items (tenant-scoped) and join each to its latest ledger entry. Pure read — safe to
    /// call repeatedly. A batch with no work-items (unknown / purged) is reported complete-with-zero so the stream
    /// closes rather than spins.
    /// </summary>
    public static async Task<Snapshot> SnapshotAsync(string batchId, CancellationToken ct = default)
    {
        var items = await PhotoProcessingJob.Query(j => j.BatchJobId == batchId, ct);
        if (items.Count == 0)
            return new Snapshot(Array.Empty<PhotoProgressEvent>(), IsComplete: true,
                new JobCompletionEvent { JobId = batchId, Status = "completed" });

        var photos = new List<PhotoProgressEvent>(items.Count);
        var records = new List<JobRecord?>(items.Count);

        // One ledger read per work-item. Bounded by the upload chunk size (≤10 files/batch in the UI), so N is small;
        // a set-of-WorkIds ledger query would collapse it to one read — the framework-lift flagged in the ADR/SURFACES.
        foreach (var item in items)
        {
            var recs = await PhotoProcessingJob.Jobs.Query(new JobQuery(WorkId: item.Id), ct);
            var rec = recs.OrderByDescending(r => r.FirstSubmittedAt).FirstOrDefault();
            records.Add(rec);
            photos.Add(new PhotoProgressEvent
            {
                JobId = batchId,
                WorkItemId = item.Id,
                PhotoId = item.PhotoId ?? "",
                FileName = item.OriginalFileName,
                Status = MapStatus(rec),
                Stage = MapStage(rec),
                // §9.7 tripwire discharged: never forward raw exception text (paths/hostnames) to the browser —
                // a job only reaches Failed on an infrastructure error (the pipeline swallows AI failures as
                // non-fatal), so a generic message is right; the detail stays in the server logs.
                Error = SafeError(rec),
            });
        }

        var allTerminal = records.All(r => r is { IsTerminal: true });
        if (!allTerminal)
            return new Snapshot(photos, IsComplete: false, Completion: null);

        var success = records.Count(r => r!.Status == JobStatus.Completed);
        var failure = records.Count - success;
        var errors = records.Select(SafeError).Where(e => e is not null).Select(e => e!).ToArray();
        var completion = new JobCompletionEvent
        {
            JobId = batchId,
            Status = failure == 0 ? "completed" : success == 0 ? "failed" : "partial-success",
            TotalPhotos = records.Count,
            SuccessCount = success,
            FailureCount = failure,
            Errors = errors,
        };
        return new Snapshot(photos, IsComplete: true, completion);
    }

    /// <summary>
    /// Stream a batch's progress as SSE: on each tick, emit a <c>PhotoProgress</c> frame per photo whose state changed
    /// since the last tick, then a single <c>JobCompleted</c> frame when the batch settles (and stop). Honors client
    /// disconnect via <paramref name="ct"/> (the request-aborted token).
    /// </summary>
    public static async IAsyncEnumerable<SseEnvelope> StreamAsync(
        string batchId, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Link the request-aborted token with the safety cap; either firing closes the stream quietly (below).
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(MaxStreamDuration);
        var token = cts.Token;

        var lastByItem = new Dictionary<string, string>(StringComparer.Ordinal);

        while (true)
        {
            Snapshot snap;
            // A client disconnect (or the safety cap) cancels the read — end the iterator cleanly, don't surface an
            // OperationCanceledException through SseResult on every normal browser close.
            try { snap = await SnapshotAsync(batchId, token); }
            catch (OperationCanceledException) { yield break; }

            foreach (var ev in snap.Photos)
            {
                // Diff on the STABLE work-item id (unique per file even pre-photo-id) so two identically-named
                // uploads never share a slot and suppress each other's frames.
                var json = JsonConvert.SerializeObject(ev, Wire);
                if (lastByItem.TryGetValue(ev.WorkItemId, out var prev) && prev == json) continue;   // unchanged → don't resend
                lastByItem[ev.WorkItemId] = json;
                yield return new SseEnvelope("PhotoProgress", json);
            }

            if (snap.IsComplete)
            {
                yield return new SseEnvelope("JobCompleted", JsonConvert.SerializeObject(snap.Completion, Wire));
                yield break;
            }

            try { await Task.Delay(PollInterval, token); }
            catch (OperationCanceledException) { yield break; }
        }
    }

    private static string MapStatus(JobRecord? rec) => rec?.Status switch
    {
        null or JobStatus.Created or JobStatus.Queued => "queued",
        JobStatus.Running => "processing",
        JobStatus.Completed => "completed",
        JobStatus.Cancelled => "cancelled",
        _ => "failed",   // Failed, Dead
    };

    private static string MapStage(JobRecord? rec)
    {
        if (!string.IsNullOrEmpty(rec?.ProgressMessage)) return rec!.ProgressMessage!;
        return MapStatus(rec);   // no ctx.Progress yet this tick → fall back to the coarse status
    }

    // A job only fails on an infrastructure error (the pipeline treats AI failures as non-fatal). Surface a
    // user-safe, generic message to the browser; the raw LastError stays in the server logs.
    private static string? SafeError(JobRecord? rec)
        => rec is { IsTerminal: true } && rec.Status is not JobStatus.Completed and not JobStatus.Cancelled
            ? "Processing failed. Please retry; if it persists, check the server logs."
            : null;
}
