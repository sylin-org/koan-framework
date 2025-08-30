namespace Sora.Web.Contracts;

/// <summary>
/// Submit draft payload.
/// </summary>
public sealed class DraftSubmit
{
    /// <summary>Optional note.</summary>
    public string? Note { get; set; }
}