using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Identity.Mfa;

/// <summary>
/// SEC-0007 P3-grp4 — a pre-provisioned, single-use account-recovery code. Codes are high-entropy CSPRNG values
/// shown to the user <b>once</b>; only a hash is stored (so a DB read cannot replay them), and each is burned on use
/// (<see cref="UsedAt"/>). Regenerating replaces the whole prior <see cref="SetId"/>. <c>IAmbientExempt</c> (global plane).
/// </summary>
public sealed class RecoveryCode : Entity<RecoveryCode>, IAmbientExempt
{
    /// <summary>The owning person.</summary>
    [Parent(typeof(Identity))]
    public string IdentityId { get; set; } = "";

    /// <summary>Hex SHA-256 of the normalized code (the code itself is never stored).</summary>
    public string CodeHash { get; set; } = "";

    /// <summary>The generation set this code belongs to — regenerating invalidates the whole prior set.</summary>
    public string SetId { get; set; } = "";

    /// <summary>Set when the code is redeemed — a code is single-use.</summary>
    public DateTimeOffset? UsedAt { get; set; }

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>True while the code may still be redeemed.</summary>
    public bool IsAvailable => UsedAt is null;
}
