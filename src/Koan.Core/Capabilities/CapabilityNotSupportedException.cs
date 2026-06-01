namespace Koan.Core.Capabilities;

/// <summary>
/// The single fail-loud signal for capability negotiation: thrown by
/// <see cref="CapabilitySet.Require"/> when a required capability was not declared. Promotes the
/// vector pathway's "an un-pushable filter node is a hard error, never silent narrowing"
/// (DATA-0097) to a framework-wide rule. See ARCH-0084.
/// </summary>
public sealed class CapabilityNotSupportedException : Exception
{
    /// <summary>Creates the exception for a missing <paramref name="capability"/>.</summary>
    public CapabilityNotSupportedException(Capability capability, string? owner = null)
        : base(owner is null
            ? $"Capability '{capability.Id}' is not supported."
            : $"'{owner}' does not support capability '{capability.Id}'.")
    {
        Capability = capability;
        Owner = owner;
    }

    /// <summary>The capability that was required but not declared.</summary>
    public Capability Capability { get; }

    /// <summary>The declaring provider's id, when known.</summary>
    public string? Owner { get; }
}
