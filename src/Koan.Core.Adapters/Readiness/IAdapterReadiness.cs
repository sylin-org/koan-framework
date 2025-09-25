namespace Koan.Core.Adapters;

public interface IAdapterReadiness
{
    AdapterReadinessState ReadinessState { get; }

    bool IsReady { get; }

    TimeSpan ReadinessTimeout { get; }

    Task<bool> IsReadyAsync(CancellationToken ct = default);

    Task WaitForReadinessAsync(TimeSpan? timeout = null, CancellationToken ct = default);

    event EventHandler<ReadinessStateChangedEventArgs>? ReadinessStateChanged;

    ReadinessStateManager StateManager { get; }
}

public interface IAdapterReadinessConfiguration
{
    ReadinessPolicy Policy { get; }

    TimeSpan Timeout { get; }

    bool EnableReadinessGating { get; }
}

public interface IAsyncAdapterInitializer
{
    Task InitializeAsync(CancellationToken ct = default);
}
