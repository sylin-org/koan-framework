namespace Koan.Core.Adapters;

public sealed class ReadinessStateManager
{
    private readonly object _gate = new();
    private AdapterReadinessState _state = AdapterReadinessState.Initializing;
    private TaskCompletionSource _readySignal = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event EventHandler<ReadinessStateChangedEventArgs>? StateChanged;

    public AdapterReadinessState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public bool IsReady
    {
        get
        {
            lock (_gate)
            {
                return _state is AdapterReadinessState.Ready or AdapterReadinessState.Degraded;
            }
        }
    }

    public void TransitionTo(AdapterReadinessState newState)
    {
        AdapterReadinessState previous;

        lock (_gate)
        {
            if (_state == newState)
            {
                return;
            }

            previous = _state;
            _state = newState;

            switch (newState)
            {
                case AdapterReadinessState.Ready:
                case AdapterReadinessState.Degraded:
                    _readySignal.TrySetResult();
                    break;
                case AdapterReadinessState.Failed:
                    _readySignal.TrySetException(new InvalidOperationException("Adapter readiness failed."));
                    break;
                case AdapterReadinessState.Initializing:
                    if (_readySignal.Task.IsCompleted)
                    {
                        _readySignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                    break;
            }
        }

        StateChanged?.Invoke(this, new ReadinessStateChangedEventArgs(previous, newState));
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct)
    {
        if (IsReady)
        {
            return Task.CompletedTask;
        }

        if (timeout <= TimeSpan.Zero)
        {
            return Task.FromException(new TimeoutException("Readiness timeout elapsed."));
        }

        return _readySignal.Task.WaitAsync(timeout, ct);
    }
}
