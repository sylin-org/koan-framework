namespace Koan.Data.Abstractions.Failures;

/// <summary>Stable provider-neutral failure categories owned by Koan Data.</summary>
public enum DataFailureKind
{
    Unknown,
    Configuration,
    PolicyDenied,
    Authentication,
    Authorization,
    Unavailable,
    Timeout,
    Conflict,
    MissingTarget,
    InvalidShape,
    Constraint,
    Conversion,
    Cancelled
}
