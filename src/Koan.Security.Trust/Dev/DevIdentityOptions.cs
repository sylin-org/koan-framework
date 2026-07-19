namespace Koan.Security.Trust.Dev;

/// <summary>
/// SEC-0001 §4 (Rung 0) — the zero-config dev identity. Evaluated only by Web Auth's Development context
/// contributor; production requests never receive this identity.
/// </summary>
public sealed class DevIdentityOptions
{
    public const string SectionPath = "Koan:Security:Trust:DevIdentity";

    /// <summary>Master switch for the dev auto-identity (lets a developer test real/anonymous auth in dev).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>The default dev subject id.</summary>
    public string Subject { get; init; } = "dev";

    /// <summary>The default dev roles. Full access by default so a simple app "just works".</summary>
    public string[] Roles { get; init; } = ["admin"];
}
