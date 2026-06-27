using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Identity;

/// <summary>
/// SEC-0007 Layer 1 — a scoped, named personal access token attached to a person. Only a hash of the secret is
/// stored (never the secret). Rotation creates a NEW row with the same scopes (provenance via
/// <see cref="RotatedFromId"/>) and revokes the old one, so a brief overlap is possible and the scope set survives.
/// </summary>
public sealed class ApiToken : Entity<ApiToken>, IAmbientExempt
{
    /// <summary>The owning person.</summary>
    [Parent(typeof(Identity))]
    public string IdentityId { get; set; } = "";

    /// <summary>Human-facing token name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The scopes this token confers.</summary>
    public List<string> Scopes { get; set; } = new();

    /// <summary>SHA-256 hash of the secret (the secret itself is shown once, at issue, and never stored).</summary>
    public string SecretHash { get; set; } = "";

    /// <summary>Optional expiry; null = no expiry.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>Last time the token was presented.</summary>
    public DateTimeOffset? LastUsedAt { get; set; }

    /// <summary>True once revoked (rotation revokes the prior token).</summary>
    public bool Revoked { get; set; }

    /// <summary>The id of the token this one rotated from (rotation provenance).</summary>
    public string? RotatedFromId { get; set; }

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>True when the token is neither revoked nor expired as of <paramref name="now"/>.</summary>
    public bool IsActive(DateTimeOffset now) => !Revoked && (ExpiresAt is null || ExpiresAt.Value > now);
}
