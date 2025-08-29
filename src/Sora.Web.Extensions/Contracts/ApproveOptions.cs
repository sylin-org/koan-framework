namespace Sora.Web.Contracts;

/// <summary>
/// Moderation approve options.
/// </summary>
public sealed class ApproveOptions
{
    /// <summary>Optional target set to write the approved entity to; defaults to current set.</summary>
    public string? TargetSet { get; set; }
    /// <summary>Free-form note for audit trails.</summary>
    public string? Note { get; set; }
    /// <summary>Optional object whose properties will be merged onto the draft before approval.</summary>
    public object? Transform { get; set; }
}