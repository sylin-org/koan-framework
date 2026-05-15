namespace Koan.Web.Auth.Roles.Options;

/// <summary>
/// Configuration for the role-management surface in <c>Koan.Web.Auth.Roles</c>. After WEB-0065
/// (auth event contributor pipeline), this options shape is purely about <i>data seeding and
/// normalization</i> for the role admin surface — it no longer drives per-request role attribution.
/// </summary>
/// <remarks>
/// <para>
/// Bound at <c>Koan:Web:Auth:Roles</c>. Apps that previously configured per-request attribution
/// options (claim keys, dev fallback, max caps, role-list path, bootstrap mode) should migrate:
/// </para>
/// <list type="bullet">
/// <item>Role-list allow/revoke file → <c>Koan:Web:Auth:Lifecycle:RoleListFile</c> in <c>Koan.Web.Auth</c></item>
/// <item>FirstUser / ClaimMatch admin bootstrap → <c>Koan:Web:Auth:Lifecycle:AdminBootstrap</c> in <c>Koan.Web.Auth</c></item>
/// <item>Claim keys / dev fallback / caps: removed; replaced by application-defined contributors</item>
/// </list>
/// </remarks>
public sealed class RoleAttributionOptions
{
    public const string SectionPath = "Koan:Web:Auth:Roles";

    /// <summary>
    /// Production guardrail for the seeding hosted service. When <see langword="false"/>
    /// (default), the seeder refuses to populate empty role/alias/policy-binding stores in
    /// Production environments. Set <see langword="true"/> in deliberately-seeded environments
    /// or use a deployment-time admin import call instead.
    /// </summary>
    public bool AllowSeedingInProduction { get; set; } = false;

    /// <summary>
    /// Alias map used by <see cref="Services.DefaultRoleConfigSnapshotProvider"/> as the initial
    /// in-memory snapshot before the DB-backed alias store is consulted. Also seeded into
    /// <c>RoleAlias</c> entities on first run.
    /// </summary>
    public AliasOptions Aliases { get; set; } = new();

    /// <summary>Role catalog seeded into the DB on first run.</summary>
    public List<RoleSeed> Roles { get; set; } = new();

    /// <summary>Policy bindings (e.g. <c>auth.roles.admin</c> → <c>role:admin</c>) seeded on first run.</summary>
    public List<RolePolicyBindingSeed> PolicyBindings { get; set; } = new();

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
