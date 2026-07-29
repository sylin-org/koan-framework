namespace Koan.Data.Abstractions.Failures;

/// <summary>Whether reacquisition or retry is safe at the failure boundary.</summary>
public enum DataRetryDisposition
{
    Never,
    BeforeDispatchOnly,
    RequiresIdempotency
}
