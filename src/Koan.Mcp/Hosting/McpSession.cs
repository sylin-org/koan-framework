using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Threading;

namespace Koan.Mcp.Hosting;

/// <summary>
/// AI-0037 — one MCP Streamable HTTP session, keyed by <c>Mcp-Session-Id</c>. Binds the authenticated principal at
/// creation (never null — an anonymous remote caller is a concrete empty principal; SamePrincipal-checked on every
/// later request so the session id is not a bearer capability) and owns the session's SSE streams: a single
/// standalone server-push GET stream plus one stream per in-flight request POST. Completed request streams are
/// retained briefly (bounded) so a resumption GET can still replay them.
/// </summary>
public sealed class McpSession : IDisposable
{
    private readonly ConcurrentDictionary<string, McpSseStream> _streams = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _retentionOrder = new();
    private readonly int _replayCapacity;
    private readonly int _maxRetainedStreams;
    private readonly TimeProvider _timeProvider;
    private long _requestStreamCounter;

    internal McpSession(
        string id,
        ClaimsPrincipal user,
        CancellationTokenSource cancellation,
        TimeProvider timeProvider,
        DateTimeOffset createdAtUtc,
        int replayCapacity,
        int maxRetainedStreams)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        User = user ?? new ClaimsPrincipal();
        Cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
        _timeProvider = timeProvider ?? TimeProvider.System;
        CreatedAtUtc = createdAtUtc;
        LastActivityUtc = createdAtUtc;
        _replayCapacity = replayCapacity;
        _maxRetainedStreams = maxRetainedStreams < 1 ? 1 : maxRetainedStreams;
    }

    public string Id { get; }
    public ClaimsPrincipal User { get; }
    public CancellationTokenSource Cancellation { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public DateTimeOffset LastActivityUtc { get; private set; }

    public void Touch() => LastActivityUtc = _timeProvider.GetUtcNow();

    /// <summary>
    /// Open the session's single standalone server-push GET stream. Returns <c>null</c> if one is already open —
    /// the MCP spec allows only one per session, and the caller answers <c>409 Conflict</c>.
    /// </summary>
    public McpSseStream? TryOpenGetStream()
    {
        var stream = new McpSseStream(McpSseStream.GetStreamId, _replayCapacity);
        return _streams.TryAdd(McpSseStream.GetStreamId, stream) ? stream : null;
    }

    /// <summary>Open a fresh per-request POST response stream with a unique id.</summary>
    public McpSseStream OpenRequestStream()
    {
        var id = "r" + Interlocked.Increment(ref _requestStreamCounter).ToString(CultureInfo.InvariantCulture);
        var stream = new McpSseStream(id, _replayCapacity);
        _streams[id] = stream;
        _retentionOrder.Enqueue(id);
        EvictOverflow();
        return stream;
    }

    public bool TryGetStream(string streamId, out McpSseStream stream)
        => _streams.TryGetValue(streamId, out stream!);

    public void CloseGetStream()
    {
        if (_streams.TryRemove(McpSseStream.GetStreamId, out var stream)) stream.Complete();
    }

    /// <summary>Complete a request stream's live channel; the stream stays retained (bounded) for resumption.</summary>
    public void CompleteRequestStream(McpSseStream stream)
    {
        if (stream is null) return;
        stream.Complete();
    }

    private void EvictOverflow()
    {
        while (_retentionOrder.Count > _maxRetainedStreams && _retentionOrder.TryDequeue(out var id))
        {
            if (_streams.TryRemove(id, out var stream)) stream.Complete();
        }
    }

    public void Dispose()
    {
        foreach (var stream in _streams.Values) stream.Complete();
        _streams.Clear();
        try { Cancellation.Dispose(); } catch { /* already disposed */ }
    }
}
