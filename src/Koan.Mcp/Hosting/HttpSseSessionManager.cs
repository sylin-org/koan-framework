using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Observability.Health;
using Koan.Mcp.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Mcp.Hosting;

public sealed class HttpSseSessionManager : IHostedService, IDisposable
{
    private readonly ConcurrentDictionary<string, HttpSseSession> _sessions = new();
    private readonly IOptionsMonitor<McpServerOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<HttpSseSessionManager> _logger;
    private readonly IHealthAggregator? _healthAggregator;
    private Timer? _heartbeatTimer;
    private readonly string _component = "mcp-http-sse";

    public HttpSseSessionManager(
        IOptionsMonitor<McpServerOptions> options,
        TimeProvider timeProvider,
        ILogger<HttpSseSessionManager> logger,
        IHealthAggregator? healthAggregator = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthAggregator = healthAggregator;
    }

    public bool TryOpenSession(HttpContext context, out HttpSseSession session)
    {
        if (context is null) throw new ArgumentNullException(nameof(context));

        var options = _options.CurrentValue;
        var limit = options.MaxConcurrentConnections;
        if (limit > 0 && _sessions.Count >= limit)
        {
            session = null!;
            return false;
        }

        var id = Guid.NewGuid().ToString("N");
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var now = _timeProvider.GetUtcNow();
        session = new HttpSseSession(id, context.User ?? new System.Security.Claims.ClaimsPrincipal(), cancellation, _timeProvider, now);

        if (!_sessions.TryAdd(id, session))
        {
            session.Dispose();
            session = null!;
            return false;
        }

        PublishHealth(options, now, _sessions.Count);
        return true;
    }

    public bool TryGet(string sessionId, out HttpSseSession session)
        => _sessions.TryGetValue(sessionId, out session!);

    public void CloseSession(HttpSseSession session)
    {
        if (session is null)
        {
            return;
        }

        if (_sessions.TryRemove(session.Id, out _))
        {
            session.Complete();
            session.Dispose();
            PublishHealth(_options.CurrentValue, _timeProvider.GetUtcNow(), _sessions.Count);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var options = _options.CurrentValue;
        var interval = options.Transport.SseKeepAliveInterval;
        if (interval <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        _heartbeatTimer = new Timer(_ => BroadcastHeartbeat(), null, interval, interval);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = null;

        foreach (var session in _sessions.Values.ToArray())
        {
            try
            {
                session.Cancellation.Cancel();
            }
            catch
            {
                // ignore
            }
            CloseSession(session);
        }

        return Task.CompletedTask;
    }

    private void BroadcastHeartbeat()
    {
        var options = _options.CurrentValue;
        var timeout = options.SseConnectionTimeout;
        var now = _timeProvider.GetUtcNow();

        foreach (var session in _sessions.Values.ToArray())
        {
            if (timeout > TimeSpan.Zero && now - session.LastActivityUtc > timeout)
            {
                _logger.LogInformation("Closing MCP HTTP+SSE session {SessionId} due to inactivity.", session.Id);
                CloseSession(session);
                continue;
            }

            session.Enqueue(ServerSentEvent.Heartbeat(now));
        }

        PublishHealth(options, now, _sessions.Count);
    }

    private void PublishHealth(McpServerOptions options, DateTimeOffset timestamp, int sessionCount)
    {
        if (_healthAggregator is null)
        {
            return;
        }

        var facts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["connections"] = sessionCount.ToString(CultureInfo.InvariantCulture),
            ["heartbeatIntervalSeconds"] = Math.Max(0, options.Transport.SseKeepAliveInterval.TotalSeconds)
                .ToString(CultureInfo.InvariantCulture)
        };

        facts["timestamp"] = timestamp.ToString("O", CultureInfo.InvariantCulture);

        _healthAggregator.Push(_component, sessionCount > 0 ? HealthStatus.Healthy : HealthStatus.Degraded, sessionCount > 0
            ? "HTTP+SSE transport active."
            : "HTTP+SSE transport idle.", facts: facts);
    }

    public void Dispose()
    {
        _heartbeatTimer?.Dispose();
    }
}
