namespace Koan.Data.Abstractions.Failures;

/// <summary>Whether the business operation itself may be replayed.</summary>
public enum DataReplayDisposition
{
    Never,
    BeforeDispatchOnly,
    RequiresIdempotency
}
