namespace Koan.Web.Auth.Roles.Options;

public sealed class RoleAttributionOptions
{
    public const string SectionPath = "Koan:Web:Auth:Roles";

    public ClaimKeyOptions ClaimKeys { get; set; } = new();
    public AliasOptions Aliases { get; set; } = new();
    public bool EmitPermissionClaims { get; set; } = true;
    public int MaxRoles { get; set; } = 256;
    public int MaxPermissions { get; set; } = 1024;
    public DevFallbackOptions DevFallback { get; set; } = new();
    public BootstrapOptions Bootstrap { get; set; } = new();
    // Import/seed guardrail: allow seeding in Production when true (disabled by default)
    public bool AllowSeedingInProduction { get; set; } = false;

    // Optional seed/template content read from configuration for import/export flows
    public List<RoleSeed> Roles { get; set; } = new();
    public List<RolePolicyBindingSeed> PolicyBindings { get; set; } = new();

    public sealed class ClaimKeyOptions
    {
        // Includes ClaimTypes.Role so AspNetCore-standard role claims (which Koan.Web.Auth's
        // AuthController emits via UserInfoMapper, and which most OIDC providers issue under the
        // `roles` scope) are picked up by attribution. Without it, providers that asserted roles
        // would be ignored and DevFallback would inject "reader" on every authenticated request.
        public string[] Roles { get; set; } = new[]
        {
            "roles",
            "role",
            "groups",
            System.Security.Claims.ClaimTypes.Role,
            Infrastructure.RoleClaimConstants.KoanRole,
            Infrastructure.RoleClaimConstants.KoanRoles,
        };
        public string[] Permissions { get; set; } = new[] { Infrastructure.RoleClaimConstants.KoanPermission, "permissions", "scope" };
    }

    public sealed class AliasOptions
    {
        public Dictionary<string, string> Map { get; set; } = new(StringComparer.OrdinalIgnoreCase)
        {
            ["administrator"] = "admin",
            ["moderator"] = "moderator",
            ["mod"] = "moderator",
            ["viewer"] = "reader",
            ["author"] = "author",
            ["editor"] = "author"
        };
    }

    public sealed class DevFallbackOptions
    {
        public bool Enabled { get; set; } = true;
        public string Role { get; set; } = "reader";
    }

    public sealed class BootstrapOptions
    {
        // Modes: None | FirstUser | ClaimMatch
        public string Mode { get; set; } = "None";
        // ClaimMatch helpers
        public string[] AdminEmails { get; set; } = [];
        public string ClaimType { get; set; } = System.Security.Claims.ClaimTypes.Email;
        public string[] ClaimValues { get; set; } = [];
    }

    public sealed class RoleSeed
    {
        public string Id { get; set; } = "";
        public string? Display { get; set; }
        public string? Description { get; set; }
    }

    public sealed class RolePolicyBindingSeed
    {
        public string Id { get; set; } = ""; // policy name
        public string Requirement { get; set; } = ""; // e.g., role:admin or perm:*
    }
}
