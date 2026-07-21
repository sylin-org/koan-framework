namespace Koan.Core.Diagnostics;

/// <summary>The observed state of a Koan runtime explanation fact.</summary>
public enum KoanFactState
{
    Unknown,
    Observed,
    Selected,
    Defaulted,
    Healthy,
    Degraded,
    Rejected,
    CollectionFailed
}
