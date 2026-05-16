namespace Koan.Web.Auth.Options;

/// <summary>
/// Options consumed by the framework's built-in <see cref="Contributors.IKoanAuthEventContributor"/>
/// implementations. Bound from configuration section <c>Koan:Web:Auth:Lifecycle</c>.
/// </summary>
public sealed class AuthLifecycleOptions
{
    public const string SectionPath = "Koan:Web:Auth:Lifecycle";

    /// <summary>
    /// Options for <see cref="Contributors.Builtin.RoleListFileContributor"/>. The contributor
    /// reads an email-keyed JSON allow/revoke file at sign-in only. Empty
    /// <see cref="RoleListFileOptions.FilePath"/> disables the contributor (default).
    /// </summary>
    public RoleListFileOptions RoleListFile { get; set; } = new();

    /// <summary>
    /// Options for <see cref="Contributors.Builtin.AdminBootstrapContributor"/>. Controls one-shot
    /// admin elevation modes (<c>FirstUser</c> / <c>ClaimMatch</c>). Default <c>Mode=None</c>
    /// disables the contributor.
    /// </summary>
    public AdminBootstrapOptions AdminBootstrap { get; set; } = new();

    /// <summary>
    /// Email-keyed role list read from a JSON file at sign-in. Operations are explicit:
    /// <c>allow</c> adds roles to the principal; <c>revoke</c> strips roles (runs after the rest
    /// of the contributor pipeline so it overrides upstream contributors). An email not present
    /// in either section is a no-op — removing from <c>allow</c> does not revoke.
    /// </summary>
    /// <remarks>
    /// File shape:
    /// <code>
    /// {
    ///   "allow":  { "user@example.com": ["admin", "curator"] },
    ///   "revoke": { "ex-admin@example.com": ["admin"] }
    /// }
    /// </code>
    /// Email lookup is case-insensitive against the principal's <see cref="System.Security.Claims.ClaimTypes.Email"/> claim.
    /// </remarks>
    public sealed class RoleListFileOptions
    {
        /// <summary>Absolute path to the JSON role list file. Empty disables the contributor.</summary>
        public string FilePath { get; set; } = "";

        /// <summary>How often the contributor may <c>stat()</c> the file to pick up edits. mtime-based reload.</summary>
        public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(30);
    }

    /// <summary>One-shot admin elevation. Modes: <c>None</c> (default) | <c>FirstUser</c> | <c>ClaimMatch</c>.</summary>
    public sealed class AdminBootstrapOptions
    {
        public string Mode { get; set; } = "None";
        public string[] AdminEmails { get; set; } = [];
        public string ClaimType { get; set; } = System.Security.Claims.ClaimTypes.Email;
        public string[] ClaimValues { get; set; } = [];
    }
}
