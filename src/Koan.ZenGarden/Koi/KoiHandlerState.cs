namespace Koan.ZenGarden.Koi;

/// <summary>
/// Lifecycle state of the background Koi topology handler.
/// </summary>
public enum KoiHandlerState
{
    /// <summary>Handler has started but has not yet probed Koi.</summary>
    Initializing,

    /// <summary>Koi daemon is not reachable. Handler re-probes periodically.</summary>
    NotDetected,

    /// <summary>SSE event stream from Koi is open and delivering topology events.</summary>
    Connected,

    /// <summary>SSE stream broke; handler is reconnecting with backoff.</summary>
    Reconnecting
}
