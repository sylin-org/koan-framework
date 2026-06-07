namespace Koan.Security.Trust.Issuer;

/// <summary>
/// SEC-0001 §6.2 — the coarse claim contract carried by a Koan credential (KSVID). Identity + broad
/// roles only; fine-grained / revocable authorization is resolved at the resource (§8), never embedded
/// in a long-lived token. This is the issuer's input DTO, decoupled from any provider-specific shape.
/// </summary>
public sealed class TrustClaims
{
    /// <summary>The stable subject identity (maps to <c>sub</c>; e.g. a user id or <c>koan://td/app-id</c>).</summary>
    public required string Subject { get; init; }

    public string? Name { get; init; }

    public string? Email { get; init; }

    /// <summary>Coarse roles (emitted as <c>ClaimTypes.Role</c> — the only claim authorization keys on today).</summary>
    public IReadOnlyCollection<string> Roles { get; init; } = [];

    /// <summary>Informational permissions (emitted as <c>Koan.permission</c>; no authorization effect — §8).</summary>
    public IReadOnlyCollection<string> Permissions { get; init; } = [];

    /// <summary>Additional pass-through claims (type → values).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Extra { get; init; }
}
