namespace Koan.Core.Adapters;

/// <summary>
/// Represents the readiness state of an external resource adapter.
/// </summary>
public enum AdapterReadinessState
{
    Initializing,
    Ready,
    Degraded,
    Failed
}

/// <summary>
/// Defines how operations behave when an adapter is not yet ready.
/// </summary>
public enum ReadinessPolicy
{
    Immediate,
    Hold,
    Degrade
}
