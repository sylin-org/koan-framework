namespace Koan.Data.Core.Events;

/// <summary>
/// Describes the aggregate disposition of a lifecycle batch operation.
/// </summary>
public enum EntityBatchDisposition
{
    Success,
    PartialSuccess,
    Cancelled
}
