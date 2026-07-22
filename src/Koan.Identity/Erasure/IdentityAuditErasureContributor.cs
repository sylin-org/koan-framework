using Koan.Data.Core;
using Koan.Identity.Audit;
using Koan.Identity.Infrastructure;

namespace Koan.Identity.Erasure;

/// <summary>Runs last and severs the erased subject from retained Identity audit evidence.</summary>
internal sealed class IdentityAuditErasureContributor : IIdentityErasureContributor
{
    private readonly AuditChain _chain;

    public IdentityAuditErasureContributor(AuditChain chain) => _chain = chain;

    public string Owner => IdentityErasureConstants.AuditOwner;
    public int Order => 1000;

    public async Task<IdentityErasureOwnerPlan> PreviewAsync(string identityId, CancellationToken ct = default)
    {
        var count = await CountRelatedAsync(identityId, ct).ConfigureAwait(false);
        return new IdentityErasureOwnerPlan(
            Owner,
            Order,
            Ready: true,
            EstimatedItems: count,
            Summary: "Sanitize retained Identity audit evidence and preserve hash-chain integrity.");
    }

    public async Task<IdentityErasureOwnerResult> EraseAsync(string identityId, CancellationToken ct = default)
    {
        var unchained = await SanitizeUnchainedAsync(identityId, ct).ConfigureAwait(false);
        var chained = await _chain.RewriteAsync(
            auditEvent => Sanitize(auditEvent, identityId),
            ct).ConfigureAwait(false);

        return new IdentityErasureOwnerResult
        {
            Owner = Owner,
            Order = Order,
            Succeeded = true,
            Summary = "Related audit evidence retained without the erased subject.",
            Counts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [IdentityErasureConstants.Counts.AuditEventsSanitized] = unchained + chained.Changed,
                [IdentityErasureConstants.Counts.ChainedEventsRehashed] = chained.Rehashed,
            },
        };
    }

    private static async Task<int> CountRelatedAsync(string identityId, CancellationToken ct)
    {
        var count = 0;
        for (var page = 1; ; page++)
        {
            var batch = await AuditEvent.Page(
                page,
                IdentityErasureConstants.PageSize,
                sort => sort.OrderBy(audit => audit.OccurredAt).ThenBy(audit => audit.Id),
                ct).ConfigureAwait(false);
            count += batch.Count(audit => IsRelated(audit, identityId));
            if (batch.Count < IdentityErasureConstants.PageSize) break;
        }
        return count;
    }

    private static async Task<int> SanitizeUnchainedAsync(string identityId, CancellationToken ct)
    {
        var changed = 0;
        for (var page = 1; ; page++)
        {
            var batch = await AuditEvent.Page(
                page,
                IdentityErasureConstants.PageSize,
                sort => sort.OrderBy(audit => audit.OccurredAt).ThenBy(audit => audit.Id),
                ct).ConfigureAwait(false);
            foreach (var auditEvent in batch.Where(static audit => audit.Hash is null))
            {
                if (!Sanitize(auditEvent, identityId)) continue;
                await auditEvent.Save(ct).ConfigureAwait(false);
                changed++;
            }
            if (batch.Count < IdentityErasureConstants.PageSize) break;
        }
        return changed;
    }

    private static bool Sanitize(AuditEvent auditEvent, string identityId)
    {
        if (!IsRelated(auditEvent, identityId)) return false;

        if (string.Equals(auditEvent.Actor, identityId, StringComparison.Ordinal)) auditEvent.Actor = null;
        if (string.Equals(auditEvent.Subject, identityId, StringComparison.Ordinal)) auditEvent.Subject = null;
        auditEvent.Target = TargetCategory(auditEvent.Target);
        auditEvent.Before = auditEvent.Before is null ? null : IdentityErasureConstants.ErasedSnapshot;
        auditEvent.After = auditEvent.After is null ? null : IdentityErasureConstants.ErasedSnapshot;
        auditEvent.Context = null;
        return true;
    }

    private static bool IsRelated(AuditEvent auditEvent, string identityId)
        => string.Equals(auditEvent.Actor, identityId, StringComparison.Ordinal)
           || string.Equals(auditEvent.Subject, identityId, StringComparison.Ordinal)
           || Contains(auditEvent.Target, identityId)
           || Contains(auditEvent.Before, identityId)
           || Contains(auditEvent.After, identityId)
           || Contains(auditEvent.Context, identityId);

    private static bool Contains(string? value, string marker)
        => value?.Contains(marker, StringComparison.Ordinal) == true;

    private static string? TargetCategory(string? target)
    {
        if (target is null) return null;
        var separator = target.IndexOf('/');
        var category = separator > 0 ? target[..separator] : target;
        return $"{category}/{IdentityErasureConstants.ErasedTarget}";
    }
}
