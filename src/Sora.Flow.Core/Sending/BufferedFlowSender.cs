using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Sora.Messaging;

namespace Sora.Flow.Sending;

// Simplified buffered sender - new messaging system handles readiness internally
internal sealed class BufferedFlowSender : IFlowSender
{
    private readonly IFlowSender _inner;
    private readonly ILogger _log;

    public BufferedFlowSender(IFlowSender inner, ILogger log)
    {
        _inner = inner;
        _log = log;
        _log.LogInformation("[msg] buffered sender initialized (new messaging system handles readiness internally)");
    }

    public bool IsReady => true; // New messaging system handles readiness internally
    public Task Ready => Task.CompletedTask;

    public Task SendAsync(IEnumerable<FlowSendItem> items, CancellationToken ct = default)
    {
        // Pass through to inner sender - new messaging system handles buffering and readiness
        return _inner.SendAsync(items, ct);
    }

    public Task SendAsync(IEnumerable<FlowSendPlainItem> items, object? envelope = null, object? message = null, Type? hostType = null, CancellationToken ct = default)
    {
        // Pass through to inner sender - new messaging system handles buffering and readiness
        return _inner.SendAsync(items, envelope, message, hostType, ct);
    }
}
