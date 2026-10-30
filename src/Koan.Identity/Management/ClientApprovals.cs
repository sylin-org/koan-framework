using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Web.Authorization;

namespace Koan.Identity.Management;

/// <summary>
/// Closes what a person approved when the person is closed. Suspend, deactivate and erase each remove the person's client
/// grants here, so their list of agents empties, every refresh family behind those grants fails, and reactivating the
/// person brings no agent back. The refresh endpoint already refuses an inactive person through their standing; this
/// is what makes the closing outlast their reactivation.
/// </summary>
public static class ClientApprovals
{
    /// <summary>
    /// Removes the client grants of <paramref name="identityId"/> a page at a time; answers how many. A person is closed
    /// everywhere, and an <see cref="AgentGrant"/> belongs to a scope, so this runs in each of them
    /// (<see cref="IMaintenanceScopes"/>): under Koan.Tenancy, every tenant, whichever is in scope or none. A store that
    /// fails surfaces.
    /// </summary>
    public static async Task<int> Revoke(string identityId, CancellationToken ct = default)
    {
        var scopes = AppHost.GetRequiredService<IMaintenanceScopes>("ClientApprovals.Revoke");
        var removed = 0;
        await scopes.ForEach(
            async token => removed += await AgentGrant.RemoveApprovals(identityId, token).ConfigureAwait(false),
            ct).ConfigureAwait(false);
        return removed;
    }
}
