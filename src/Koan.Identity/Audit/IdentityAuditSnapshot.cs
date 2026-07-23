using Koan.Identity.Impersonation;
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
        _ => JsonConvert.SerializeObject(new { Entity = entity.GetType().Name }),
    };
}
