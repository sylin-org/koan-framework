using System;

namespace Koan.Tenancy.Web;

/// <summary>
/// The layered activation config for the tenancy control-plane console (ARCH-0104), mirroring the composable
/// tenant-resolution carriers — but with the routing/authority line held: <b>request-shape governs EXPOSURE
/// (where/whether the console appears), never AUTHORITY (who may operate it)</b>. Bound from
/// <c>Koan:Tenancy:Console</c>. Resolution is strictly layered and fail-closed: <see cref="Enabled"/> →
/// <see cref="Exposure"/> (404 if unmatched) → posture + <see cref="Grant"/> (403 if unadmitted) → 200.
/// </summary>
public sealed class TenancyConsoleOptions
{
    public const string SectionPath = "Koan:Tenancy:Console";

    /// <summary>Kill-switch: when false the console API + UI are not served at all (404). Default true (Reference = Intent).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The exposure (routing) layer — decides IF/WHERE the console responds. Forgeable signals; never authority.</summary>
    public ConsoleExposureOptions Exposure { get; set; } = new();

    /// <summary>The grant (authority) layer — decides WHO may operate. Keyed on identity/role, never request-shape.</summary>
    public ConsoleGrantOptions Grant { get; set; } = new();

    /// <summary>
    /// Defense-in-depth for the dev-open bypass: when true, the Open-posture (Development) auto-admit applies only to
    /// loopback requests, so a dev host bound to a public interface does not expose an ungated console. Default false
    /// (preserves the plain dev-open just-works behavior).
    /// </summary>
    public bool RequireLoopbackForOpenPosture { get; set; }
}

/// <summary>The exposure (routing) layer. All conditions must match for the console to respond; an unmatched request
/// gets a 404 (the surface is "not here"), not a 403.</summary>
public sealed class ConsoleExposureOptions
{
    /// <summary>Host allow-list (e.g. <c>ops.acme.com</c>); empty = any host. Case-insensitive, host only (no port).</summary>
    public string[] Hosts { get; set; } = Array.Empty<string>();

    /// <summary>Optional header that must be present for the console to respond (e.g. <c>X-Koan-Console</c>); null = none.
    /// A routing signal only — never treat its presence as authority.</summary>
    public string? RequireHeader { get; set; }
}

/// <summary>The grant (authority) layer — composed OR: either an identity in <see cref="Operators"/> (config
/// break-glass) or a principal carrying <see cref="Role"/> is admitted. Empty list + no role claim = nobody (fail-closed).</summary>
public sealed class ConsoleGrantOptions
{
    /// <summary>Break-glass operator identities (email / <c>sub</c> / name) admitted even in prod-closed posture. By
    /// identity, NOT request-shape — a host config grant, never derived from a tenant membership ("no master backdoor").</summary>
    public string[] Operators { get; set; } = Array.Empty<string>();

    /// <summary>The role claim that admits (e.g. bound via <c>Koan.Identity</c>'s <c>IdentityRole</c>, or any auth scheme).</summary>
    public string Role { get; set; } = TenancyRoles.Operator;
}

/// <summary>The fixed console mount paths (ARCH-0104). Path <i>relocation</i> (a configurable prefix) is a follow-on —
/// it needs coordinated route-convention + UI-relative-path changes; v1 pins these and composes host/header exposure.</summary>
public static class TenancyConsolePaths
{
    /// <summary>The bundled UI path.</summary>
    public const string UiPath = "/tenancy";

    /// <summary>The operator API base path.</summary>
    public const string ApiPath = "/api/tenancy/admin";
}
