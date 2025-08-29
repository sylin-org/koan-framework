namespace Sora.Web.Contracts;

/// <summary>
/// Restore options.
/// </summary>
public sealed class RestoreOptions
{
    /// <summary>Optional note.</summary>
    public string? Note { get; set; }
    /// <summary>Optional target set to restore into; defaults to current set.</summary>
    public string? TargetSet { get; set; }
}