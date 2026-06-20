using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Koan.Mcp.Options;
using Koan.Web.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Mcp.Hosting;

/// <summary>
/// AI-0037 — the registry of live MCP Streamable HTTP sessions, keyed by <c>Mcp-Session-Id</c>. Mints a session at
/// <c>initialize</c> (binding the OriginStamp-applied principal), resolves it on subsequent requests, terminates it
/// on DELETE or idle, and reclaims idle sessions on a sweep. Unlike the legacy single-request SSE session, a
/// Streamable session outlives any one request — its cancellation is session-scoped, not tied to a request abort.
/// </summary>
public sealed class McpSessionManager : IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<string, McpSession> _sessions = new(StringComparer.Ordinal);
    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<McpSessionManager> _logger;
    private Timer? _sweepTimer;

    public McpSessionManager(
        IOptionsMonitor<McpServerOptions> options,
        TimeProvider timeProvider,
        ILogger<McpSessionManager> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public int Count => _sessions.Count;

    /// <summary>
    /// Mint a new session for an <c>initialize</c> request. Returns <c>null</c> if the concurrent-session cap is
    /// reached (the caller answers 429). The session id is a 128-bit CSPRNG value rendered as 32 hex chars
    /// (visible-ASCII, per the spec); the principal is stamped with its origin tier once, here, at the edge.
    /// </summary>
    public McpSession? Create(HttpContext context)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var options = _options.CurrentValue;
        var limit = options.MaxConcurrentConnections;
        if (limit > 0 && _sessions.Count >= limit)
        {
            return null;
        }

        var id = Guid.NewGuid().ToString("N");
        var now = _timeProvider.GetUtcNow();

        // SEC-0004 origin: a remote (HTTP) caller is never `local` — stamp the session principal `internal` when its
        // source IP is in a declared trusted network, else `remote` (the safe default). Stamped ONCE here, so every
        // request this session makes carries the correct origin.
        var originOptions = context.RequestServices.GetService<IOptions<OriginOptions>>()?.Value ?? OriginOptions.Empty;
        var principal = OriginStamp.Apply(
            context.User ?? new ClaimsPrincipal(),
            OriginResolver.FromHttpContext(context, originOptions));

        var session = new McpSession(
            id,
            principal,
            new CancellationTokenSource(),
            _timeProvider,
            now,
            options.Transport.StreamReplayBufferSize,
            options.Transport.MaxRetainedStreamsPerSession);

        return _sessions.TryAdd(id, session) ? session : null;
    }

    public bool TryGet(string sessionId, out McpSession session)
        => _sessions.TryGetValue(sessionId, out session!);

    /// <summary>Terminate a session (DELETE, idle, or shutdown). Returns false if the id was unknown.</summary>
    public bool Terminate(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId) || !_sessions.TryRemove(sessionId, out var session))
        {
            return false;
        }

        try { session.Cancellation.Cancel(); } catch { /* ignore */ }
        session.Dispose();
        return true;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var interval = _options.CurrentValue.Transport.SseKeepAliveInterval;
        if (interval > TimeSpan.Zero)
        {
            _sweepTimer = new Timer(_ => Sweep(), null, interval, interval);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _sweepTimer?.Dispose();
        _sweepTimer = null;
        foreach (var id in _sessions.Keys.ToArray())
        {
            Terminate(id);
        }
        return Task.CompletedTask;
    }

    private void Sweep()
    {
        var timeout = _options.CurrentValue.SseConnectionTimeout;
        if (timeout <= TimeSpan.Zero) return;

        var now = _timeProvider.GetUtcNow();
        foreach (var session in _sessions.Values.ToArray())
        {
            if (now - session.LastActivityUtc > timeout)
            {
                _logger.LogInformation("Reclaiming idle MCP session {SessionId}.", session.Id);
                Terminate(session.Id);
            }
        }
    }

    public void Dispose() => _sweepTimer?.Dispose();
}
