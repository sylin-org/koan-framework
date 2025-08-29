namespace Sora.Web.Contracts;

/// <summary>
/// Update draft payload.
/// </summary>
public sealed class DraftUpdate
{
    /// <summary>Partial object to merge onto the existing draft snapshot.</summary>
    public object? Snapshot { get; set; }
    /// <summary>Optional note.</summary>
    public string? Note { get; set; }
}