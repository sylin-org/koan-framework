namespace Sora.Web.Extensions.Authorization;

public sealed class AuditPolicy
{
    public string? Snapshot { get; set; }
    public string? List { get; set; }
    public string? Revert { get; set; }
}