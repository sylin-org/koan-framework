namespace Sora.Web.Extensions.Authorization;

public sealed class CapabilityPolicy
{
    public ModerationPolicy Moderation { get; set; } = new();
    public SoftDeletePolicy SoftDelete { get; set; } = new();
    public AuditPolicy Audit { get; set; } = new();
}