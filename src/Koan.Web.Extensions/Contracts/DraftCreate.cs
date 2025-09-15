namespace Koan.Web.Contracts;

/// <summary>
/// Create draft payload.
/// </summary>
public sealed class DraftCreate
{
    /// <summary>Optional initial snapshot for the draft; merged as-is into the entity.</summary>
    public object? Snapshot { get; set; }
    /// <summary>Optional note.</summary>
    public string? Note { get; set; }
}