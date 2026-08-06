using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Identity.Impersonation;
using Koan.Identity.Infrastructure;

namespace Koan.Identity.Erasure;

/// <summary>Closes access first, then removes every core-owned durable-person row.</summary>
internal sealed class IdentityCoreErasureContributor : IIdentityErasureContributor
{
    public string Owner => IdentityErasureConstants.CoreOwner;
    public int Order => 100;

    public async Task<IdentityErasureOwnerPlan> PreviewAsync(string identityId, CancellationToken ct = default)
    {
        var owned = await LoadAsync(identityId, ct).ConfigureAwait(false);
        var count = (owned.Person is null ? 0 : 1) + owned.Emails.Count + owned.Sessions.Count +
                    owned.ExternalLinks.Count + owned.Roles.Count + owned.ImpersonationGrants.Count;
        return new IdentityErasureOwnerPlan(
            Owner,
            Order,
            Ready: true,
            EstimatedItems: count,
            Summary: "Deactivate the person, close cookie access, and remove core identity dependents.");
    }

    public async Task<IdentityErasureOwnerResult> EraseAsync(string identityId, CancellationToken ct = default)
    {
        var owned = await LoadAsync(identityId, ct).ConfigureAwait(false);

        // Fail closed before cleanup. If a later owner fails, ordinary Koan cookie validation already rejects this
        // person. Repeating the operation is safe when the row has already advanced or disappeared.
        if (owned.Person is not null && owned.Person.Status != IdentityStatus.Deactivated)
        {
            owned.Person.Status = IdentityStatus.Deactivated;
            await owned.Person.Save(ct).ConfigureAwait(false);
        }

        var emailCount = await RemoveAll(owned.Emails, ct).ConfigureAwait(false);
        var sessionCount = await RemoveAll(owned.Sessions, ct).ConfigureAwait(false);
        var linkCount = await RemoveAll(owned.ExternalLinks, ct).ConfigureAwait(false);
        var roleCount = await RemoveAll(owned.Roles, ct).ConfigureAwait(false);
        var impersonationCount = await RemoveAll(owned.ImpersonationGrants, ct).ConfigureAwait(false);
        var identityCount = owned.Person is not null && await owned.Person.Remove(ct).ConfigureAwait(false) ? 1 : 0;

        return new IdentityErasureOwnerResult
        {
            Owner = Owner,
            Order = Order,
            Succeeded = true,
            Summary = "Core identity records erased and cookie access closed.",
            Counts = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                [IdentityErasureConstants.Counts.Identities] = identityCount,
                [IdentityErasureConstants.Counts.Emails] = emailCount,
                [IdentityErasureConstants.Counts.Sessions] = sessionCount,
                [IdentityErasureConstants.Counts.ExternalLinks] = linkCount,
                [IdentityErasureConstants.Counts.GlobalRoles] = roleCount,
                [IdentityErasureConstants.Counts.ImpersonationGrants] = impersonationCount,
            },
        };
    }

    private static async Task<OwnedRows> LoadAsync(string identityId, CancellationToken ct)
    {
        var person = await Identity.Get(identityId, ct).ConfigureAwait(false);
        var emails = await IdentityEmail.Query(email => email.IdentityId == identityId, ct).ConfigureAwait(false);
        var sessions = await Session.Query(session => session.IdentityId == identityId, ct).ConfigureAwait(false);
        var links = await ExternalIdentityLink.Query(link => link.IdentityId == identityId, ct).ConfigureAwait(false);
        var roles = await IdentityRole.Query(role => role.IdentityId == identityId, ct).ConfigureAwait(false);
        var impersonation = await ImpersonationGrant.Query(
            grant => grant.Actor == identityId || grant.Target == identityId,
            ct).ConfigureAwait(false);
        return new OwnedRows(person, emails, sessions, links, roles, impersonation);
    }

    private static async Task<int> RemoveAll<TEntity>(IReadOnlyList<TEntity> rows, CancellationToken ct)
        where TEntity : Entity<TEntity>
    {
        var removed = 0;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            if (await row.Remove(ct).ConfigureAwait(false)) removed++;
        }

        if (removed != rows.Count)
            throw new InvalidOperationException(
                $"{typeof(TEntity).Name} erasure did not remove every selected row. Retry the identity erasure.");
        return removed;
    }

    private sealed record OwnedRows(
        Identity? Person,
        IReadOnlyList<IdentityEmail> Emails,
        IReadOnlyList<Session> Sessions,
        IReadOnlyList<ExternalIdentityLink> ExternalLinks,
        IReadOnlyList<IdentityRole> Roles,
        IReadOnlyList<ImpersonationGrant> ImpersonationGrants);
}
