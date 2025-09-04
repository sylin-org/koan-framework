using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sora.Messaging.Provisioning;

public interface IMessagingReadinessProvider
{
    bool IsReady { get; }
    MessagingReadinessState GetState();
    void SetReady();
    void SetPending(string reason);
}

public sealed class MessagingReadinessProvider : IMessagingReadinessProvider
{
    private volatile bool _isReady;
    private volatile string? _pendingReason;
    public bool IsReady => _isReady;
    public MessagingReadinessState GetState() => new MessagingReadinessState(_isReady, _pendingReason);
    public void SetReady() { _isReady = true; _pendingReason = null; }
    public void SetPending(string reason) { _isReady = false; _pendingReason = reason; }
}

public record MessagingReadinessState(bool IsReady, string? PendingReason);
