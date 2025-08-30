namespace Sora.Web.Contracts;

/// <summary>
/// Withdraw draft payload.
/// </summary>
public sealed class DraftWithdraw
{
    /// <summary>Optional reason for withdrawing.</summary>
    public string? Reason { get; set; }
}