namespace Koan.Core.Capabilities;

/// <summary>
/// The declaration face a provider receives to describe what it can do. Fluent and additive:
/// a token may stand alone, or carry one strongly-typed detail value (the only structured detail
/// in the framework today is <c>FilterSupport</c>). The framework then negotiates against the
/// resulting <see cref="CapabilitySet"/>. See ARCH-0084.
/// </summary>
public interface ICapabilities
{
    /// <summary>Declare support for a capability that carries no structured detail.</summary>
    ICapabilities Add(Capability token);

    /// <summary>Declare support for a capability together with a strongly-typed detail value.</summary>
    ICapabilities Add<TDetail>(Capability token, TDetail detail) where TDetail : notnull;
}
