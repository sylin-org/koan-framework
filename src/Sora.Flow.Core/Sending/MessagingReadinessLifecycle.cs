
using System;
using System.Threading.Tasks;
using Sora.Messaging.Provisioning;

namespace Sora.Flow.Sending;

public sealed class MessagingReadinessLifecycle
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private volatile bool _ready;
    public IMessagingReadinessProvider ReadinessProvider { get; }
    public MessagingReadinessLifecycle(IMessagingReadinessProvider readinessProvider)
    {
        ReadinessProvider = readinessProvider;
    }
    public bool IsReady => _ready;
    public Task Ready => _tcs.Task;
    public void SignalReady()
    {
        if (_ready) return;
        _ready = true;
        _tcs.TrySetResult();
    }
}