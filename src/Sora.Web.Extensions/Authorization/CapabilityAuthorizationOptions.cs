namespace Sora.Web.Extensions.Authorization;

public sealed class CapabilityAuthorizationOptions
{
    public CapabilityDefaultBehavior DefaultBehavior { get; set; } = CapabilityDefaultBehavior.Allow;
    public CapabilityPolicy Defaults { get; set; } = new();
    public Dictionary<string, CapabilityPolicy> Entities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}