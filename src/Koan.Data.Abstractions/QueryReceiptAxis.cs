namespace Koan.Data.Abstractions;

/// <summary>The query execution axis whose provider receipt is inconsistent with the request.</summary>
public enum QueryReceiptAxis
{
    Filter,
    Sort,
    Pagination,
    Projection,
    Count,
    Bound
}
