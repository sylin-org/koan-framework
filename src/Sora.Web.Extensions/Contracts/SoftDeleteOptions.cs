namespace Sora.Web.Contracts;

/// <summary>
/// Soft delete options.
/// </summary>
public sealed class SoftDeleteOptions
{
    /// <summary>Optional note.</summary>
    public string? Note { get; set; }
    /// <summary>Optional source set to move from; if not set, deletes from the current set.</summary>
    public string? FromSet { get; set; }
}