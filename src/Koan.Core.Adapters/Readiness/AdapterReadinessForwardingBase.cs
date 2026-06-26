using System;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Core.Adapters;

/// <summary>
/// The readiness surface every server-backed adapter forwards to its connection provider (ARCH-0103 cross-cutting
/// promotion). The ~10 <see cref="IAdapterReadiness"/> members delegate to an abstract <see cref="Readiness"/> provider;
/// the configuration (policy / timeout / gating) is supplied by the subclass. Server-backed family bases (the document
/// base today; server-vector / relational can adopt) inherit this instead of repeating the forwarding block. In-proc
/// families (KeyValueStore) have no readiness and do NOT inherit it.
/// </summary>
public abstract class AdapterReadinessForwardingBase : IAdapterReadiness, IAdapterReadinessConfiguration
{
    /// <summary>The connection provider that holds the actual readiness state.</summary>
    protected abstract IAdapterReadiness Readiness { get; }

    public AdapterReadinessState ReadinessState => Readiness.ReadinessState;
    public bool IsReady => Readiness.IsReady;
    public TimeSpan ReadinessTimeout => Readiness.ReadinessTimeout;
    public ReadinessStateManager StateManager => Readiness.StateManager;
    public Task<bool> IsReadyAsync(CancellationToken ct = default) => Readiness.IsReadyAsync(ct);
    public Task WaitForReadiness(TimeSpan? timeout = null, CancellationToken ct = default) => Readiness.WaitForReadiness(timeout, ct);

    public event EventHandler<ReadinessStateChangedEventArgs>? ReadinessStateChanged
    {
        add => Readiness.ReadinessStateChanged += value;
        remove => Readiness.ReadinessStateChanged -= value;
    }

    public abstract ReadinessPolicy Policy { get; }
    public abstract TimeSpan Timeout { get; }
    public abstract bool EnableReadinessGating { get; }
}
