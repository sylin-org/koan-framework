using Koan.Identity.Impersonation;
using Koan.Identity.Roles;
using Newtonsoft.Json;

namespace Koan.Identity.Audit;

/// <summary>One owner for the audit snapshot privacy posture selected by <see cref="IdentityOptions"/>.</summary>
internal static class IdentityAuditSnapshot
{
    public static string? Serialize(object? entity, IdentityAuditSnapshotMode mode) => entity switch
    {
        null => null,
        ExternalIdentityLink link when mode == IdentityAuditSnapshotMode.Full => JsonConvert.SerializeObject(new
        {
            link.Id,
            link.IdentityId,
            link.Provider,
            link.ProviderKeyHash,
            link.CreatedAt,
            ClaimsJson = link.ClaimsJson is null ? null : "[redacted]",
        }),
        _ when mode == IdentityAuditSnapshotMode.Full => JsonConvert.SerializeObject(entity),
        Identity person => JsonConvert.SerializeObject(new
        {
            Status = person.Status.ToString(),
            person.CreatedAt,
            person.UpdatedAt,
        }),
        IdentityEmail email => JsonConvert.SerializeObject(new
        {
            email.Verified,
            email.Primary,
            email.CreatedAt,
        }),
        ExternalIdentityLink link => JsonConvert.SerializeObject(new
        {
            link.Provider,
            link.CreatedAt,
            HasProviderClaims = link.ClaimsJson is not null,
        }),
        Session session => JsonConvert.SerializeObject(new
        {
            session.Revoked,
            session.RevokedAt,
            session.FirstSeen,
            session.LastActive,
        }),
        IdentityRole role => JsonConvert.SerializeObject(new
        {
            role.RoleKey,
            role.CreatedAt,
        }),
        ImpersonationGrant grant => JsonConvert.SerializeObject(new
        {
            grant.ApprovedAt,
            grant.ExpiresAt,
            grant.Revoked,
            grant.RequestedAt,
        }),
        ScopedRoleScope scope => JsonConvert.SerializeObject(new
        {
            scope.TenantId, scope.Type, scope.ScopeId, scope.OwnerSubject, scope.ParentType, scope.ParentScopeId, scope.Version,
        }),
        ScopedRoleDefinition definition => JsonConvert.SerializeObject(new
        {
            definition.TenantId, definition.ScopeType, definition.ScopeId, definition.Status,
            definition.Version, definition.AuthorityVersion,
            Capabilities = definition.Grants.Select(x => x.Capability).Distinct(StringComparer.Ordinal).Order(),
        }),
        ScopedRoleBinding binding => JsonConvert.SerializeObject(new
        {
            binding.TenantId, binding.Subject, binding.RoleId, binding.ScopeType, binding.ScopeId,
            binding.Propagation, binding.ExpiresAt, binding.Revoked, binding.Version,
            binding.ApprovedRoleVersion, PolicyVersions = binding.ApprovedPolicyVersions.Count,
        }),
        ScopedRolePolicy policy => JsonConvert.SerializeObject(new
        {
            policy.TenantId, policy.ScopeType, policy.ScopeId, policy.Capability, policy.Mode,
            policy.Version, AudienceKinds = policy.Audience.Select(x => x.Kind).Distinct().Order(),
        }),
        _ => JsonConvert.SerializeObject(new { Entity = entity.GetType().Name }),
    };
}
