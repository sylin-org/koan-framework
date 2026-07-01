namespace S6.SnapVault.Progress;

/// <summary>
/// The upload-progress wire contract (SnapVault D4). These are the two Server-Sent Event payloads the browser's
/// <c>EventSource</c> consumes on <c>GET /api/photos/progress/{batchId}</c> — the SSE replacement for the old
/// SignalR <c>PhotoProgress</c> / <c>JobCompleted</c> hub messages. Field names are the same the legacy client
/// read (serialized camelCase), so the visual monitor is unchanged; only the transport moved from a stateful hub
/// to a stateless read-projection of the durable jobs ledger.
/// </summary>
public sealed record PhotoProgressEvent
{
    /// <summary>The upload batch id (what the browser subscribed to) — the legacy <c>job:{jobId}</c> group key.</summary>
    public string JobId { get; init; } = "";

    /// <summary>The processing work-item id (the <c>PhotoProcessingJob</c> id) — the STABLE per-file key. Distinct
    /// for every file even before a photo id exists, so two identically-named uploads never collapse into one row
    /// (the client and the stream-diff both key on this).</summary>
    public string WorkItemId { get; init; } = "";

    /// <summary>The processed photo's id once known (assigned mid-ingest); empty during the early stages.</summary>
    public string PhotoId { get; init; } = "";

    /// <summary>The original upload file name (from the work-item), so the monitor can label the row before a photo id exists.</summary>
    public string FileName { get; init; } = "";

    /// <summary>Coarse lifecycle: <c>queued</c> · <c>processing</c> · <c>completed</c> · <c>failed</c> · <c>cancelled</c>
    /// — derived from the job's ledger <see cref="Koan.Jobs.JobStatus"/>.</summary>
    public string Status { get; init; } = "";

    /// <summary>The fine-grained stage (see <see cref="PhotoProcessingStage"/>) carried by the ledger's
    /// <c>ProgressMessage</c>; the rebuilt ingest pipeline emits it via <c>ctx.Progress(fraction, stage)</c> (step 5).</summary>
    public string Stage { get; init; } = "";

    /// <summary>The ledger's <c>LastError</c> when the job failed; null otherwise.</summary>
    public string? Error { get; init; }
}

/// <summary>Terminal SSE frame: emitted once, when every job in the batch has settled, then the stream closes.</summary>
public sealed record JobCompletionEvent
{
    public string JobId { get; init; } = "";

    /// <summary><c>completed</c> when all succeeded, <c>partial-success</c> when some failed, <c>failed</c> when all failed.</summary>
    public string Status { get; init; } = "";

    public int TotalPhotos { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();
}

/// <summary>
/// The stage vocabulary the ingest pipeline reports through <c>ctx.Progress(fraction, stage)</c> — the durable,
/// tenant-scoped progress the SSE projection reads straight off the ledger. Named here in step 4 as the contract;
/// the rebuilt <c>ProcessUpload</c> emits these in step 5 (the emit points live inside the pipeline, which is why
/// they can't run before the ingest service is registered). The UI maps each to a friendly label.
/// </summary>
public static class PhotoProcessingStage
{
    public const string Queued = "queued";
    public const string Upload = "upload";
    public const string Exif = "exif";
    public const string Thumbnails = "thumbnails";
    public const string AiDescription = "ai-description";
    public const string Embedding = "embedding";
    public const string Completed = "completed";
}
