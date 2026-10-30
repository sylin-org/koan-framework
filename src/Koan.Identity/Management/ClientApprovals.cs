using Koan.Core.Semantics.Segmentation;
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
    /// Removes the client grants of <paramref name="identityId"/> a page at a time; answers how many. A
    /// <see cref="AgentGrant"/> is tenant-scoped, so with Koan.Tenancy closed and no tenant in scope there are none to
    /// reach from here. A store that fails surfaces.
    /// </summary>
    public static async Task<int> Revoke(string identityId, CancellationToken ct = default)
    {
        try
        {
            return await AgentGrant.RemoveApprovals(identityId, ct).ConfigureAwait(false);
        }
        catch (SegmentationRequiredException)
        {
            return 0;
        }
    }
}
