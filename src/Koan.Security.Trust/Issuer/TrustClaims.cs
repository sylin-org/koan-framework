namespace Koan.Security.Trust.Issuer;

/// <summary>
/// The coarse claim contract carried by a Koan workload token. Identity + broad
/// roles only; fine-grained / revocable authorization is resolved at the resource (§8), never embedded
/// in a long-lived token. This is the issuer's input DTO, decoupled from any provider-specific shape.
/// </summary>
public sealed class TrustClaims
{
    /// <summary>The stable subject identity (maps to <c>sub</c>; e.g. a user id or <c>koan://td/app-id</c>).</summary>
    public required string Subject { get; init; }

    public string? Name { get; init; }

    public string? Email { get; init; }

    /// <summary>Coarse roles (emitted as <c>ClaimTypes.Role</c>).</summary>
    public IReadOnlyCollection<string> Roles { get; init; } = [];

    /// <summary>
    /// OAuth scopes granted to this credential — the authorization grant. Emitted as the RFC 9068 §2.2.3
    /// <c>scope</c> claim (one space-delimited value), which is what the SEC-0004 <c>[Access(has:scope:x)]</c>
    /// gate and the custom <c>[McpTool(RequiredScopes)]</c> policy read. (Unlike <see cref="Permissions"/>, scopes
    /// DO carry authorization effect — they are the grant.)
    /// </summary>
    public IReadOnlyCollection<string> Scopes { get; init; } = [];

    /// <summary>Informational permissions (emitted as <c>Koan.permission</c>; no authorization effect — §8).</summary>
    public IReadOnlyCollection<string> Permissions { get; init; } = [];

    /// <summary>Additional pass-through claims (type → values).</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Extra { get; init; }
}
