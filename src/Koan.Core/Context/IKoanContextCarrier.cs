namespace Koan.Core.Context;

/// <summary>
/// A module-owned serializer for one opaque Koan context axis. A host registers carriers through DI so durable work
/// can capture logical-flow state and later restore or explicitly suppress it without the transport naming the axis.
/// </summary>
public interface IKoanContextCarrier
{
    /// <summary>
    /// Stable lowercase ASCII identity used as the persisted carrier-bag key. It must be unique within one host.
    /// </summary>
    string AxisKey { get; }

    /// <summary>The weakest ingress provenance from which this axis may be restored.</summary>
    ContextIngressTrust MinimumIngressTrust { get; }

    /// <summary>
    /// Captures the current value as an opaque, carrier-versioned string, or <c>null</c> when the axis is absent.
    /// </summary>
    string? Capture();

    /// <summary>
    /// Validates and restores <paramref name="captured"/> for the lifetime of the returned scope. Reject malformed or
    /// unsupported formats with the safe factories on <see cref="KoanContextCarrierException"/> before pushing state.
    /// </summary>
    IDisposable Restore(string captured);

    /// <summary>
    /// Explicitly removes this axis for the returned scope so durable work cannot inherit a worker thread's context.
    /// </summary>
    IDisposable Suppress();
}
