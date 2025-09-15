namespace Koan.Web.Extensions.Authorization;

public sealed class ModerationPolicy
{
    public string? DraftCreate { get; set; }
    public string? DraftUpdate { get; set; }
    public string? DraftGet { get; set; }
    public string? Submit { get; set; }
    public string? Withdraw { get; set; }
    public string? Queue { get; set; }
    public string? Approve { get; set; }
    public string? Reject { get; set; }
    public string? Return { get; set; }
}