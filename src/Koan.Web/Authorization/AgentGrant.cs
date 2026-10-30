using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Sorting;

namespace Koan.Web.Authorization;

/// <summary>
/// SEC-0005 — a server-side, queryable, revocable grant of a capability to an agent, BEYOND whatever its token
/// carries. When the token alone is denied a gated action, the gate materializes the subject's active grants as
/// scoped effective-claims and re-evaluates (see <see cref="EntityFloorAuthorizationProvider"/>), so a grant
/// satisfies the coarse <c>Needs</c> without bypassing row <c>Constrain</c>. Grants are entities — the Koan move:
/// <c>Save()</c> to issue, <c>Remove()</c> to revoke (fleet-wide on the next call), <see cref="ExpiresAt"/> to
/// time-box, <c>Query(...)</c> to observe. A grant Koan's authorization server issues is one approval of one client,
/// and <see cref="Approvals"/> reads a person's live ones.
/// </summary>
public sealed class AgentGrant : Entity<AgentGrant>
{
    private const string SubjectExpiryIndex = "ix_agent_grants_subject_expiry";
    private const int ApprovalRemovalPageSize = 100;

    /// <summary>The agent's subject id — the principal's <c>sub</c> (bearer) or <c>NameIdentifier</c> (cookie).</summary>
    [Index(Group = SubjectExpiryIndex, Order = 0)]
    public string Subject { get; set; } = "";

    /// <summary>An <c>[Access]</c> term the grant confers — <c>"is:admin"</c> / <c>"has:scope:orders:fulfill"</c> /
    /// <c>"has:claim:tier=pro"</c>. <c>owner</c> is NOT grantable (ownership is row-bound <c>Constrain</c>, not a
    /// claim); <c>anyone</c>/<c>authenticated</c>/<c>origin</c> are not capabilities an agent can be lent.</summary>
    public string Capability { get; set; } = "";

    /// <summary>The entity name the grant applies to, or <c>"*"</c> for any.</summary>
    public string Resource { get; set; } = "*";

    /// <summary>Optional expiry; <c>null</c> = no expiry.</summary>
    [Index(Group = SubjectExpiryIndex, Order = 1)]
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>When the grant was issued (UTC). Koan's issuers set it: the authorization server when a person approves
    /// a client, and <c>JitGrantService</c>. A grant an application saves itself carries what the application sets.
    /// <c>null</c> for a grant issued before Koan recorded it.</summary>
    public DateTimeOffset? IssuedAt { get; set; }

    /// <summary>The <c>client_id</c> of the OAuth client the authorization server issued this grant to; <c>null</c> for a
    /// grant issued any other way, or before Koan recorded it.</summary>
    public string? ClientId { get; set; }

    /// <summary>The name that client registered (RFC 7591 <c>client_name</c>) when the grant was issued; <c>null</c> when
    /// it registered none or the grant has no client. The client chose it and nothing verified it: show it as unverified,
    /// and fall back to <see cref="ClientId"/>.</summary>
    public string? ClientName { get; set; }

    /// <summary>
    /// The live approvals of <paramref name="subject"/>: the unexpired grants Koan's authorization server issued to a
    /// client for them, one per approval, newest first, at most <paramref name="take"/>. Each revokes alone with
    /// <c>Remove()</c>, which also makes the client ask the person again before it is remembered. The filter on subject
    /// and expiry is the query's, so expired rows are not returned; a connector that builds the declared
    /// <c>(Subject, ExpiresAt)</c> index reads only the subject's live rows, while one that builds none (the in-memory
    /// connector scans; SQL Server declines a <c>string</c> key) reads more to answer the same. A grant issued before
    /// Koan recorded its client has no <see cref="ClientId"/> and is not among them.
    /// </summary>
    public static Task<IReadOnlyList<AgentGrant>> Approvals(string subject, int take, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        ArgumentOutOfRangeException.ThrowIfLessThan(take, 1);
        var now = DateTimeOffset.UtcNow;
        var newestFirst = new QueryDefinition()
            .WithSort<AgentGrant>(sort => sort.OrderByDescending(g => g.IssuedAt))
            .WithPagination(1, take);
        return Query(g => g.Subject == subject && g.ExpiresAt > now && g.ClientId != null, newestFirst, ct);
    }

    /// <summary>
    /// Removes every grant Koan's authorization server issued to a client for <paramref name="subject"/>, live or expired,
    /// a page at a time; answers how many this call removed. Each removal runs as <c>Remove()</c> does, so each client asks
    /// the person again before it is remembered. A grant issued any other way is left. Removing the grants is what ends
    /// an agent's renewals for good; access it already holds lasts out its short lifetime.
    /// </summary>
    public static async Task<int> RemoveApprovals(string subject, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        var page = new QueryDefinition().WithPagination(1, ApprovalRemovalPageSize);
        var removed = 0;
        while (true)
        {
            var rows = await Query(g => g.Subject == subject && g.ClientId != null, page, ct);
            var progress = 0;
            foreach (var row in rows)
                if (await row.Remove(ct)) progress++;
            removed += progress;

            // Removing shrinks the set, so the next read is the next page. A page that removed nothing ends the walk:
            // it was empty, or another revocation of this person took those rows and is walking the rest.
            if (progress == 0) return removed;
        }
    }

    /// <summary>True when the grant has not expired as of <paramref name="now"/>.</summary>
    public bool IsActive(DateTimeOffset now) => ExpiresAt is null || ExpiresAt.Value > now;

    /// <summary>True when this grant applies to <paramref name="resourceName"/> (exact match or the <c>"*"</c> wildcard).</summary>
    public bool AppliesTo(string resourceName)
        => Resource == "*" || string.Equals(Resource, resourceName, StringComparison.OrdinalIgnoreCase);
}
