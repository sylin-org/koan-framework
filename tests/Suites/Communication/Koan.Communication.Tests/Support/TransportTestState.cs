using System.Collections.Concurrent;

namespace Koan.Communication.Tests.Support;

public sealed class TransportTestState
{
    public ConcurrentQueue<string> CopyObservations { get; } = new();
    public ConcurrentQueue<string> BlockingObservations { get; } = new();
    public ConcurrentQueue<int> SequenceObservations { get; } = new();
    public ConcurrentQueue<string?> TenantObservations { get; } = new();

    public TaskCompletionSource<bool> BlockingStarted { get; } = Signal();
    public TaskCompletionSource<bool> BlockingRelease { get; } = Signal();
    public TaskCompletionSource<bool> CancellationStarted { get; } = Signal();
    public TaskCompletionSource<bool> CancellationRelease { get; } = Signal();
    public TaskCompletionSource<bool> DrainStarted { get; } = Signal();
    public TaskCompletionSource<bool> DrainRelease { get; } = Signal();

    public int FilterHandled;
    public int CancellationHandled;
    public int DrainHandled;
    public int IsolationHandled;

    private static TaskCompletionSource<bool> Signal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
