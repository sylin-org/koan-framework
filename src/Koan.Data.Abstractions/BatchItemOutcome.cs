namespace Koan.Data.Abstractions;

/// <summary>The provider-neutral result of one queued batch operation.</summary>
public enum BatchItemOutcome
{
    Applied,
    Missing,
    Conflict,
    Unknown
}
