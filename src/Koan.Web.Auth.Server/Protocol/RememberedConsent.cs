using Koan.Data.Core.Model;

namespace Koan.Web.Auth.Server.Protocol;

/// <summary>
/// SEC-0006 D9 — a person's remembered consent for one client, scope set and resource. The row id is a hash of the
/// subject, the grant's capability term and the resource, so it holds no identity in the clear. The consent page
/// records it when the person approves, for as long as the grant that approval issues; a re-connect it lets through
/// does not extend it. Removing any live grant for the same key forgets it (SEC-0005), so the next connection asks
/// the person again while their other approvals keep working. Expired rows are swept with the other artifacts.
/// </summary>
public sealed class RememberedConsent : Entity<RememberedConsent>
{
    /// <summary>When the consent stops being remembered: the expiry of the grant the person's approval issued.</summary>
    public DateTimeOffset ExpiresUtc { get; set; }

    public bool IsLive(DateTimeOffset now) => ExpiresUtc > now;
}
