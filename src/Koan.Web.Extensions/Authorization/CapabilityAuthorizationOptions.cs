namespace Koan.Web.Extensions.Authorization;

public sealed class CapabilityAuthorizationOptions
{
    /// <summary>
    /// What an action with no mapped policy does (WEB-0083). Deny is the default: declaring a capability controller opens
    /// no authority by itself. <see cref="CapabilityDefaultBehavior.Allow"/> opens every unmapped action to any signed-in
    /// caller, and is an application's explicit choice.
    /// </summary>
    public CapabilityDefaultBehavior DefaultBehavior { get; set; } = CapabilityDefaultBehavior.Deny;
    public CapabilityPolicy Defaults { get; set; } = new();
    public Dictionary<string, CapabilityPolicy> Entities { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}