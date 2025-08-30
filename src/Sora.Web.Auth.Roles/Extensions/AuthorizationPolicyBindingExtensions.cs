using Microsoft.Extensions.DependencyInjection;
using Sora.Web.Extensions.Policies;

namespace Sora.Web.Auth.Roles.Extensions;

public static class AuthorizationPolicyBindingExtensions
{
    public sealed class RolePolicyOptions
    {
        public bool UseDefaults { get; set; } = true;
        public string AdminRole { get; set; } = "admin";
        public string ModeratorRole { get; set; } = "moderator";
        public string AuthorRole { get; set; } = "author";
        public string AuditRole { get; set; } = "admin";
        public string SoftDeleteRole { get; set; } = "moderator";
    }

    public static IServiceCollection AddSoraRolePolicies(this IServiceCollection services, System.Action<RolePolicyOptions>? configure = null)
    {
        var opts = new RolePolicyOptions();
        configure?.Invoke(opts);

        if (!opts.UseDefaults) return services;

        // Reuse Web.Extensions helper to add simple role-bound policies
        services.AddSoraWebCapabilityPolicies(p =>
        {
            p.ModerationAuthorRole = opts.AuthorRole;
            p.ModerationReviewerRole = opts.ModeratorRole;
            p.ModerationPublisherRole = opts.AdminRole;
            p.SoftDeleteRole = opts.SoftDeleteRole;
            p.AuditRole = opts.AuditRole;
        });

        return services;
    }
}
