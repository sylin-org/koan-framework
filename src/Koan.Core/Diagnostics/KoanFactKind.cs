namespace Koan.Core.Diagnostics;

/// <summary>The stable shared meaning of a Koan runtime explanation fact.</summary>
public enum KoanFactKind
{
    Discovery,
    Dependency,
    Election,
    Capability,
    Guarantee,
    Default,
    Degradation,
    Rejection,
    Health,
    Correction
}
