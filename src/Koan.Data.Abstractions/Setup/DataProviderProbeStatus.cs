namespace Koan.Data.Abstractions;

/// <summary>The strongest fact established by a candidate-source probe.</summary>
public enum DataProviderProbeStatus
{
    Ready,
    Reachable,
    InvalidConfiguration,
    Unavailable,
    TimedOut,
    Unsupported
}
