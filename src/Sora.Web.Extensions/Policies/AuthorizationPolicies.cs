using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Sora.Web.Extensions.Policies;

/// <summary>
/// Canonical policy names for capability controllers. Apps should bind these to roles/claims.
/// </summary>
public static class SoraWebPolicyNames
{
    public const string ModerationAuthor = "moderation.author";
    public const string ModerationReviewer = "moderation.reviewer";
    public const string ModerationPublisher = "moderation.publisher";
    public const string SoftDeleteActor = "softdelete.actor";
    public const string AuditActor = "audit.actor";
}

/// <summary>
/// Optional helper to register basic capability policies mapped to roles. Opt-in only.
/// </summary>
public static class AuthorizationPolicyExtensions
{
    public sealed class CapabilityAuthorizationOptions
    {
        public string? ModerationAuthorRole { get; set; }
        public string? ModerationReviewerRole { get; set; }
        public string? ModerationPublisherRole { get; set; }
        public string? SoftDeleteRole { get; set; }
        public string? AuditRole { get; set; }
    }

    /// <summary>
    /// Adds named policies using simple role requirements. Provide role names you want to map.
    /// Any null/empty mapping is skipped (policy not added).
    /// </summary>
    public static IServiceCollection AddSoraWebCapabilityPolicies(
        this IServiceCollection services,
        System.Action<CapabilityAuthorizationOptions> configure)
    {
        var opts = new CapabilityAuthorizationOptions();
        configure?.Invoke(opts);

        services.AddAuthorization(options =>
        {
            if (!string.IsNullOrWhiteSpace(opts.ModerationAuthorRole))
                options.AddPolicy(SoraWebPolicyNames.ModerationAuthor, p => p.RequireRole(opts.ModerationAuthorRole!));
            if (!string.IsNullOrWhiteSpace(opts.ModerationReviewerRole))
                options.AddPolicy(SoraWebPolicyNames.ModerationReviewer, p => p.RequireRole(opts.ModerationReviewerRole!));
            if (!string.IsNullOrWhiteSpace(opts.ModerationPublisherRole))
                options.AddPolicy(SoraWebPolicyNames.ModerationPublisher, p => p.RequireRole(opts.ModerationPublisherRole!));
            if (!string.IsNullOrWhiteSpace(opts.SoftDeleteRole))
                options.AddPolicy(SoraWebPolicyNames.SoftDeleteActor, p => p.RequireRole(opts.SoftDeleteRole!));
            if (!string.IsNullOrWhiteSpace(opts.AuditRole))
                options.AddPolicy(SoraWebPolicyNames.AuditActor, p => p.RequireRole(opts.AuditRole!));
        });

        return services;
    }
}
