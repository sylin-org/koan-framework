using System;

namespace Koan.Mcp.Options;

public sealed class McpTransportOptions
{
    private TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(30);
    private TimeSpan _shutdownTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Interval used by transports to emit heartbeat diagnostics.
    /// </summary>
    public TimeSpan HeartbeatInterval
    {
        get => _heartbeatInterval;
        set => _heartbeatInterval = value <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : value;
    }

    /// <summary>
    /// Grace period granted to transports when shutting down.
    /// </summary>
    public TimeSpan ShutdownTimeout
    {
        get => _shutdownTimeout;
        set => _shutdownTimeout = value <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : value;
    }

    /// <summary>
    /// Logging category used by MCP transports when emitting structured diagnostics.
    /// </summary>
    public string LoggerCategory { get; set; } = "Koan.Transport.Mcp";

    private TimeSpan _sseKeepAliveInterval = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Size of the buffer used when flushing SSE payloads.
    /// </summary>
    public int SseBufferSize { get; set; } = 8192;

    /// <summary>
    /// Interval used to publish keep-alive heartbeats on SSE streams.
    /// </summary>
    public TimeSpan SseKeepAliveInterval
    {
        get => _sseKeepAliveInterval;
        set => _sseKeepAliveInterval = value <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : value;
    }

    private int _streamReplayBufferSize = 256;

    /// <summary>
    /// AI-0037 — per-stream replay buffer capacity for MCP Streamable HTTP resumability: the number of recent SSE
    /// events each stream retains so a resumption GET carrying <c>Last-Event-ID</c> can replay that stream's tail.
    /// <c>0</c> disables resumability (no <c>id:</c> lines are buffered). Default 256.
    /// </summary>
    public int StreamReplayBufferSize
    {
        get => _streamReplayBufferSize;
        set => _streamReplayBufferSize = value < 0 ? 0 : value;
    }

    /// <summary>
    /// AI-0037 — when true, a Streamable HTTP POST carrying a JSON-RPC request is answered as a single
    /// <c>application/json</c> object instead of the default per-request <c>text/event-stream</c> response. The SSE
    /// response is the spec default (and the only shape that can carry interim notifications before the result);
    /// JSON is the simpler shape for stateless request/response clients. Default false (SSE-per-request).
    /// </summary>
    public bool StreamableJsonResponse { get; set; } = false;

    private int _maxRetainedStreamsPerSession = 64;

    /// <summary>
    /// AI-0037 — the maximum number of completed per-request POST streams a session retains for resumption replay
    /// before evicting the oldest (the long-lived standalone GET stream is never evicted). Bounds session memory.
    /// Default 64.
    /// </summary>
    public int MaxRetainedStreamsPerSession
    {
        get => _maxRetainedStreamsPerSession;
        set => _maxRetainedStreamsPerSession = value < 1 ? 1 : value;
    }
}
