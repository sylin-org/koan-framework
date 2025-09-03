using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sora.Flow.Sending;

/// <summary>
/// Bridges messaging readiness signaled by the core messaging layer (via AppDomain flags)
/// to the Flow <see cref="MessagingReadinessLifecycle"/> so buffered senders can flush.
/// </summary>
internal sealed class MessagingReadinessBridgeHostedService : IHostedService
{
    private readonly MessagingReadinessLifecycle _lifecycle;
    private readonly ILogger<MessagingReadinessBridgeHostedService>? _log;
    private const string ReadyFlag = "Sora.Messaging.Ready";
    private const string PendingFlag = "Sora.Messaging.Readiness.Pending";

    public MessagingReadinessBridgeHostedService(MessagingReadinessLifecycle lifecycle, ILogger<MessagingReadinessBridgeHostedService>? log = null)
    { _lifecycle = lifecycle; _log = log; }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // If already ready, fast-path.
        if (AppDomain.CurrentDomain.GetData(ReadyFlag) is bool r && r)
        {
            _log?.LogDebug("[flow.msg] readiness already signaled (fast-path)");
            _lifecycle.SignalReady();
            return Task.CompletedTask;
        }

        _ = Task.Run(async () =>
        {
            const int maxChecks = 240; // up to ~120s (@500ms)
            for (var i = 0; i < maxChecks && !cancellationToken.IsCancellationRequested; i++)
            {
                if (AppDomain.CurrentDomain.GetData(ReadyFlag) is bool ready && ready)
                {
                    _log?.LogInformation("[flow.msg] messaging readiness bridged after {Checks} checks (~{Seconds}s)", i + 1, (i + 1) * 0.5);
                    _lifecycle.SignalReady();
                    return;
                }
                await Task.Delay(500, cancellationToken).ConfigureAwait(false);
            }
            if (!_lifecycle.IsReady)
            {
                var pending = AppDomain.CurrentDomain.GetData(PendingFlag) is bool p && p;
                _log?.LogWarning("[flow.msg] messaging readiness not achieved within timeout (pending={Pending}) – buffer will continue holding items until process exit", pending);
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
