namespace Koan.Web.Extensions.Authorization;

/// <summary>What a capability action with no mapped policy does. Deny unless an application says otherwise.</summary>
public enum CapabilityDefaultBehavior
{
    Deny,
    Allow
}
