namespace Sora.Web.Contracts;

/// <summary>
/// Moderation rejection payload.
/// </summary>
public sealed class RejectOptions
{
    /// <summary>Required human-readable reason for rejection.</summary>
    public required string Reason { get; set; }
    /// <summary>Optional additional note.</summary>
    public string? Note { get; set; }
}