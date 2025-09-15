using Microsoft.AspNetCore.Authorization;

namespace Koan.Web.Extensions.Policies;

/// <summary>
/// Canonical policy names for capability controllers. Apps should bind these to roles/claims.
/// </summary>
public static class KoanWebPolicyNames
{
    public const string ModerationAuthor = "moderation.author";
    public const string ModerationReviewer = "moderation.reviewer";
    public const string ModerationPublisher = "moderation.publisher";
    public const string SoftDeleteActor = "softdelete.actor";
    public const string AuditActor = "audit.actor";
}