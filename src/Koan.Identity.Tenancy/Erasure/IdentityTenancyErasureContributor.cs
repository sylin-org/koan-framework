using Koan.Data.Core;
using Koan.Identity.Erasure;
using Koan.Identity.Tenancy.Deprovisioning;
using Koan.Identity.Tenancy.Infrastructure;
using Koan.Tenancy;
using Koan.Web.Authorization;

namespace Koan.Identity.Tenancy.Erasure;

/// <summary>Removes tenant access owned by the bridge and de-identifies its retained lifecycle evidence.</summary>
internal sealed class IdentityTenancyErasureContributor : IIdentityErasureContributor
{
    public string Owner => IdentityTenancyErasureConstants.Owner;
    public int Order => IdentityTenancyErasureConstants.Order;

    public async Task<IdentityErasureOwnerPlan> PreviewAsync(string identityId, CancellationToken ct = default)
    {
        var memberships = await Membership.Query(row => row.IdentityId == identityId, ct).ConfigureAwait(false);
        var receipts = await DeprovisioningReceipt.Query(row => row.IdentityId == identityId, ct).ConfigureAwait(false);
        var grants = await LoadAgentGrantsAsync(identityId, ct).ConfigureAwait(false);
        var auditCount = await CountRelatedAuditAsync(identityId, ct).ConfigureAwait(false);
        return new IdentityErasureOwnerPlan(
            Owner,
            Order,
            Ready: true,
            EstimatedItems: memberships.Count + receipts.Count + grants.Count + auditCount,
            Summary: "Remove tenant memberships and agent grants; de-identify retained tenancy evidence.");
    }

    public async Task<IdentityErasureOwnerResult> EraseAsync(string identityId, CancellationToken ct = default)
    {
        var grants = await LoadAgentGrantsAsync(identityId, ct).ConfigureAwait(false);
        var grantCount = 0;
        foreach (var owned in grants)
        {
            using (Tenant.Use(owned.TenantId))
            {
                if (await owned.Grant.Remove(ct).ConfigureAwait(false)) grantCount++;
            }
        }
        EnsureRemoved(grantCount, grants.Count, nameof(AgentGrant));

        var memberships = await Membership.Query(row => row.IdentityId == identityId, ct).ConfigureAwait(false);
        var membershipCount = 0;
        foreach (var membership in memberships)
            if (await membership.Remove(ct).ConfigureAwait(false)) membershipCount++;
        EnsureRemoved(membershipCount, memberships.Count, nameof(Membership));

        var receipts = await DeprovisioningReceipt.Query(row => row.IdentityId == identityId, ct).ConfigureAwait(false);
        foreach (var receipt in receipts)
        {
            receipt.IdentityId = "";
            receipt.Hash = receipt.ComputeHash();
            await receipt.Save(ct).ConfigureAwait(false);
        }

        var auditCount = await SanitizeAuditAsync(identityId, ct).ConfigureAwait(false);
        return new IdentityErasureOwnerResult
        {
            Owner = Owner,
            Order = Order,
            Succeeded = true,
            Summary = "Tenant access removed and retained tenancy evidence de-identified.",
            Counts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [IdentityTenancyErasureConstants.Counts.Memberships] = membershipCount,
                [IdentityTenancyErasureConstants.Counts.AgentGrants] = grantCount,
                [IdentityTenancyErasureConstants.Counts.DeprovisioningReceipts] = receipts.Count,
                [IdentityTenancyErasureConstants.Counts.TenantAuditEntries] = auditCount,
            },
        };
    }

    private static async Task<List<OwnedAgentGrant>> LoadAgentGrantsAsync(string identityId, CancellationToken ct)
    {
        var owned = new List<OwnedAgentGrant>();
        foreach (var tenantId in await LoadTenantIdsAsync(ct).ConfigureAwait(false))
        {
            using (Tenant.Use(tenantId))
            {
                var grants = await AgentGrant.Query(row => row.Subject == identityId, ct).ConfigureAwait(false);
                owned.AddRange(grants.Select(grant => new OwnedAgentGrant(tenantId, grant)));
            }
        }
        return owned;
    }

    private static async Task<List<string>> LoadTenantIdsAsync(CancellationToken ct)
    {
        var tenantIds = new List<string>();
        for (var page = 1; ; page++)
        {
            var batch = await TenantRecord.Page(
                page,
                IdentityTenancyErasureConstants.PageSize,
                sort => sort.OrderBy(row => row.Id),
                ct).ConfigureAwait(false);
            tenantIds.AddRange(batch.Select(static row => row.Id));
            if (batch.Count < IdentityTenancyErasureConstants.PageSize) break;
        }
        return tenantIds;
    }

    private static async Task<int> CountRelatedAuditAsync(string identityId, CancellationToken ct)
    {
        var count = 0;
        await ForEachAuditPageAsync(
            batch =>
            {
                count += batch.Count(row => IsRelated(row, identityId));
                return Task.CompletedTask;
            },
            ct).ConfigureAwait(false);
        return count;
    }

    private static async Task<int> SanitizeAuditAsync(string identityId, CancellationToken ct)
    {
        var changed = 0;
        await ForEachAuditPageAsync(
            async batch =>
            {
                foreach (var audit in batch.Where(row => IsRelated(row, identityId)))
                {
                    if (string.Equals(audit.Actor, identityId, StringComparison.Ordinal))
                        audit.Actor = IdentityTenancyErasureConstants.ErasedActor;
                    audit.Summary = IdentityTenancyErasureConstants.ErasedSummary;
                    await audit.Save(ct).ConfigureAwait(false);
                    changed++;
                }
            },
            ct).ConfigureAwait(false);
        return changed;
    }

    private static async Task ForEachAuditPageAsync(
        Func<IReadOnlyList<TenantAuditEntry>, Task> visit,
        CancellationToken ct)
    {
        for (var page = 1; ; page++)
        {
            var batch = await TenantAuditEntry.Page(
                page,
                IdentityTenancyErasureConstants.PageSize,
                sort => sort.OrderBy(row => row.At).ThenBy(row => row.Id),
                ct).ConfigureAwait(false);
            await visit(batch).ConfigureAwait(false);
            if (batch.Count < IdentityTenancyErasureConstants.PageSize) break;
        }
    }

    private static bool IsRelated(TenantAuditEntry audit, string identityId)
        => string.Equals(audit.Actor, identityId, StringComparison.Ordinal)
           || audit.Summary.Contains(identityId, StringComparison.Ordinal);

    private static void EnsureRemoved(int removed, int selected, string entity)
    {
        if (removed != selected)
            throw new InvalidOperationException(
                $"{entity} erasure did not remove every selected row. Retry the identity erasure.");
    }

    private sealed record OwnedAgentGrant(string TenantId, AgentGrant Grant);
}
