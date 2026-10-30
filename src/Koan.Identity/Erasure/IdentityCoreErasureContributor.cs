using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Identity.Impersonation;
using Koan.Identity.Infrastructure;
using Koan.Identity.Management;
using Koan.Identity.Roles;
using Microsoft.Extensions.Options;

namespace Koan.Identity.Erasure;

/// <summary>Closes access first, then removes every core-owned durable-person row.</summary>
internal sealed class IdentityCoreErasureContributor(RoleCollection roles, IOptions<RoleOptions> roleOptions) : IIdentityErasureContributor
{
    public string Owner => IdentityErasureConstants.CoreOwner;
    public int Order => 100;

    public async Task<IdentityErasureOwnerPlan> PreviewAsync(string identityId, CancellationToken ct = default)
    {
        var owned = await LoadAsync(identityId, ct).ConfigureAwait(false);
        var memberships = await RoleMember.Count.Where(member => member.PersonId == identityId, CountStrategy.Exact, ct).ConfigureAwait(false);
        var overwrites = await PermissionOverwrite.Count.Where(
            row => row.SubjectKind == OverwriteSubjectKind.Person && row.SubjectId == identityId, CountStrategy.Exact, ct).ConfigureAwait(false);
        var count = (owned.Person is null ? 0L : 1L) + owned.Emails.Count + owned.Sessions.Count +
                    owned.ExternalLinks.Count + memberships + overwrites + owned.ImpersonationGrants.Count;
        return new IdentityErasureOwnerPlan(
            Owner,
            Order,
            Ready: true,
            EstimatedItems: (int)Math.Min(count, int.MaxValue),
            Summary: "Deactivate the person, close cookie access, remove their client grants, and remove core identity dependents.");
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

        var clientGrantCount = await ClientApprovals.Revoke(identityId, ct).ConfigureAwait(false);
        var emailCount = await RemoveAll(owned.Emails, ct).ConfigureAwait(false);
        var sessionCount = await RemoveAll(owned.Sessions, ct).ConfigureAwait(false);
        var linkCount = await RemoveAll(owned.ExternalLinks, ct).ConfigureAwait(false);
        // Every membership row is removed, a page at a time, however many there are: never through the read that
        // refuses a person holding more roles than the bound, so erasure always finishes.
        var roleCount = 0;
        for (string? after = null; ;)
        {
            var (memberships, next) = await RoleReads.MembershipPage(identityId, after, roleOptions.Value.MaxPageSize, ct).ConfigureAwait(false);
            foreach (var membership in memberships)
            {
                if (await roles.Remove(membership.RoleKey, identityId, ct).ConfigureAwait(false)) roleCount++;
            }

            if ((after = next) is null) break;
        }

        var overwriteCount = 0;
        for (string? after = null; ;)
        {
            var page = await roles.OverwritesOf(OverwriteSubjectKind.Person, identityId, after, roleOptions.Value.MaxPageSize, ct).ConfigureAwait(false);
            foreach (var overwrite in page.Items)
            {
                if (await roles.RemoveOverwrite(overwrite.ResourceId, OverwriteSubjectKind.Person, identityId, ct).ConfigureAwait(false))
                    overwriteCount++;
            }

            if ((after = page.Next) is null) break;
        }
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
                [IdentityErasureConstants.Counts.PermissionOverwrites] = overwriteCount,
                [IdentityErasureConstants.Counts.ImpersonationGrants] = impersonationCount,
                [IdentityErasureConstants.Counts.ClientGrants] = clientGrantCount,
            },
        };
    }

    private async Task<OwnedRows> LoadAsync(string identityId, CancellationToken ct)
    {
        var person = await Identity.Get(identityId, ct).ConfigureAwait(false);
        var emails = await IdentityEmail.Query(email => email.IdentityId == identityId, ct).ConfigureAwait(false);
        var sessions = await Session.Query(session => session.IdentityId == identityId, ct).ConfigureAwait(false);
        var links = await ExternalIdentityLink.Query(link => link.IdentityId == identityId, ct).ConfigureAwait(false);
        var impersonation = await ImpersonationGrant.Query(
            grant => grant.Actor == identityId || grant.Target == identityId,
            ct).ConfigureAwait(false);
        return new OwnedRows(person, emails, sessions, links, impersonation);
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
        IReadOnlyList<ImpersonationGrant> ImpersonationGrants);
}
