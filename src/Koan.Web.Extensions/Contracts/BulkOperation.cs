namespace Koan.Web.Contracts;

/// <summary>
/// Bulk operation shape for batch actions.
/// </summary>
public sealed class BulkOperation<TId>
{
    /// <summary>IDs to include in the operation; either Ids or Filter must be provided.</summary>
    public IReadOnlyList<TId>? Ids { get; set; }
    /// <summary>Optional string-query filter; requires a string-query capable repository.</summary>
    public string? Filter { get; set; }
    /// <summary>Operation-specific options (e.g., FromSet/TargetSet).</summary>
    public object? Options { get; set; }
}