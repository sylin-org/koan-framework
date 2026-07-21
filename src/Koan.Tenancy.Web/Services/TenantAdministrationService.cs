using Koan.Data.Core;

namespace Koan.Tenancy.Web.Services;

/// <summary>
/// The single mutation chokepoint for the supported tenancy control plane: tenant registry changes and membership
/// grants/revocations. Every completed mutation writes a <see cref="TenantAuditEntry"/>.
/// </summary>
public sealed class TenantAdministrationService
{
    /// <summary>Create a tenant. Throws when its normalized routing code is already in use.</summary>
    public async Task<TenantRecord> CreateTenant(
        string actor,
        string name,
        string? code,
        CancellationToken ct = default)
    {
        var normalizedName = Required(name, nameof(name));
        var normalizedCode = NormalizeCode(code);
        await EnsureCodeAvailable(normalizedCode, exceptTenantId: null, ct).ConfigureAwait(false);

        var tenant = await new TenantRecord
        {
            Name = normalizedName,
            Code = normalizedCode,
        }.Save(ct).ConfigureAwait(false);

        await TenantAuditEntry.Record(
            actor,
            "tenant.created",
            tenant.Id,
            normalizedCode is null
                ? $"created '{tenant.Name}'"
                : $"created '{tenant.Name}' with code '{normalizedCode}'",
            ct).ConfigureAwait(false);
        return tenant;
    }

    /// <summary>Rename a tenant without changing its durable id or routing code.</summary>
    public async Task<TenantRecord?> RenameTenant(
        string actor,
        string tenantId,
        string name,
        CancellationToken ct = default)
    {
        var tenant = await TenantRecord.Get(tenantId, ct).ConfigureAwait(false);
        if (tenant is null) return null;

        var normalizedName = Required(name, nameof(name));
        if (string.Equals(tenant.Name, normalizedName, StringComparison.Ordinal)) return tenant;

        var previous = tenant.Name;
        tenant.Name = normalizedName;
        await tenant.Save(ct).ConfigureAwait(false);
        await TenantAuditEntry.Record(
            actor,
            "tenant.renamed",
            tenant.Id,
            $"'{previous}' → '{tenant.Name}'",
            ct).ConfigureAwait(false);
        return tenant;
    }

    /// <summary>
    /// Grant or replace one subject's tenant roles. The deterministic seat id makes repeated equivalent grants
    /// converge to the same row. Host roles are never valid membership roles.
    /// </summary>
    public async Task<Membership?> GrantMembership(
        string actor,
        string tenantId,
        string identityId,
        IEnumerable<string>? roles,
        CancellationToken ct = default)
    {
        if (await TenantRecord.Get(tenantId, ct).ConfigureAwait(false) is null) return null;

        var subject = Required(identityId, nameof(identityId));
        var normalizedRoles = (roles ?? [TenancyRoles.Member])
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .Select(role => role.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(role => role, StringComparer.Ordinal)
            .ToList();
        if (normalizedRoles.Count == 0) normalizedRoles.Add(TenancyRoles.Member);
        if (normalizedRoles.Any(TenancyRoles.IsReservedHostRole))
            throw new ArgumentException("Host operator roles cannot be granted through a tenant membership.", nameof(roles));

        var id = Membership.KeyFor(tenantId, subject);
        var membership = await Membership.Get(id, ct).ConfigureAwait(false)
                         ?? new Membership { Id = id, TenantId = tenantId, IdentityId = subject };
        var changed = !membership.Roles.SequenceEqual(normalizedRoles, StringComparer.Ordinal);
        membership.Roles = normalizedRoles;
        await membership.Save(ct).ConfigureAwait(false);

        if (changed)
            await TenantAuditEntry.Record(
                actor,
                "membership.granted",
                tenantId,
                $"granted {subject}: {string.Join(", ", normalizedRoles)}",
                ct).ConfigureAwait(false);
        return membership;
    }

    /// <summary>Revoke one membership. Returns false when it is already absent.</summary>
    public async Task<bool> RevokeMembership(
        string actor,
        string membershipId,
        CancellationToken ct = default)
    {
        var membership = await Membership.Get(membershipId, ct).ConfigureAwait(false);
        if (membership is null) return false;

        await membership.Remove(ct).ConfigureAwait(false);
        await TenantAuditEntry.Record(
            actor,
            "membership.revoked",
            membership.TenantId,
            $"revoked {membership.IdentityId}",
            ct).ConfigureAwait(false);
        return true;
    }

    private static async Task EnsureCodeAvailable(string? code, string? exceptTenantId, CancellationToken ct)
    {
        if (code is null) return;
        var matches = await TenantRecord.Query(t => t.Code == code, ct).ConfigureAwait(false);
        if (matches.Any(t => !string.Equals(t.Id, exceptTenantId, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Tenant code '{code}' is already in use.");
    }

    private static string Required(string? value, string parameter)
        => string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("A non-empty value is required.", parameter)
            : value.Trim();

    private static string? NormalizeCode(string? code)
        => string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToLowerInvariant();
}
