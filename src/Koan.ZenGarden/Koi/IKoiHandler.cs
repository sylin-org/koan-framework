namespace Koan.ZenGarden.Koi;

/// <summary>
/// Background topology handler that observes the local mDNS landscape via Koi.
/// Publishes an immutable snapshot and fires events on topology changes.
/// </summary>
internal interface IKoiHandler : IDisposable
{
    /// <summary>Current handler lifecycle state.</summary>
    KoiHandlerState State { get; }

    /// <summary>
    /// Current topology snapshot. Immutable; safe to read from any thread.
    /// Returns <see cref="KoiTopologySnapshot.Empty"/> before first probe completes.
    /// </summary>
    KoiTopologySnapshot CurrentSnapshot { get; }

    /// <summary>
    /// Subscribe to topology change events. Returns a handle that unsubscribes on dispose.
    /// Handlers are invoked sequentially with error isolation (one failure does not block others).
    /// </summary>
    IDisposable OnTopologyEvent(Func<KoiTopologyEvent, CancellationToken, ValueTask> handler);

    /// <summary>
    /// Starts the background discovery loop. Safe to call multiple times; only the first call takes effect.
    /// </summary>
    void Start();
}
