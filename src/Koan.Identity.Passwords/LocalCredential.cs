using Koan.Core;
using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace Koan.Identity.Passwords;

/// <summary>
/// SEC-0007 P3-grp4 (D4 opt-in / D2 portable) — a person's local password factor. The <see cref="PasswordHash"/> is a
/// <b>portable, self-describing</b> BCrypt PHC/MCF string (one column; algorithm + cost + salt + digest), so it is
/// import/export-able across languages with no bespoke auth-storage adapter. One password per person (deterministic
/// id), looked up at sign-in by <see cref="LoginIdentifier"/> (a normalized email). <c>IAmbientExempt</c> (global plane).
/// </summary>
public sealed class LocalCredential : Entity<LocalCredential>, IAmbientExempt
{
    /// <summary>The owning person (the canonical <see cref="Identity"/> id).</summary>
    [Parent(typeof(Identity))]
    public string IdentityId { get; set; } = "";

    /// <summary>The normalized login handle (email) the credential is looked up by at sign-in.</summary>
    public string LoginIdentifier { get; set; } = "";

    /// <summary>The portable PHC/MCF password hash.</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>The hasher scheme that produced the hash (diagnostics / migration); e.g. <c>bcrypt</c>.</summary>
    public string Scheme { get; set; } = "";

    /// <summary>Set once, on creation.</summary>
    [Timestamp]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Updated on every save (password set / rehash).</summary>
    [Timestamp(OnSave = true)]
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>One local password per person — re-setting upserts the one row instead of duplicating.</summary>
    public static string KeyFor(string identityId) => DeterministicId.From("local-password", identityId);
}
