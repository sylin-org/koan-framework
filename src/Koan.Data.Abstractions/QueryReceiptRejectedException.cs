namespace Koan.Data.Abstractions;

/// <summary>Rejects an incomplete or impossible provider query-execution receipt.</summary>
public sealed class QueryReceiptRejectedException : InvalidOperationException
{
    public QueryReceiptRejectedException(
        string entityType,
        QueryReceiptAxis axis,
        string correction)
        : base(
            $"The query receipt for '{entityType}' does not prove {axis.ToString().ToLowerInvariant()} execution. " +
            correction)
    {
        EntityType = entityType;
        Axis = axis;
        Correction = correction;
    }

    public string EntityType { get; }
    public QueryReceiptAxis Axis { get; }
    public string Correction { get; }
}
