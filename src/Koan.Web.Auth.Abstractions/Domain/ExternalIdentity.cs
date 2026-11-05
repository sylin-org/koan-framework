namespace Koan.Web.Auth.Domain;

/// <summary>
/// A login: one identity a provider vouched for, belonging to the person it is bound to. The person is the store's to
/// know; a login names only its provider and its subject.
/// </summary>
public sealed class ExternalIdentity
{
    /// <summary>
    /// The provider's subject, as the login arrived from a sign-in. A store keeps only <see cref="ProviderKeyHash"/>, so a
    /// login read back from one carries none.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>The provider that vouched for the login (e.g. <c>google</c>, <c>atproto</c>).</summary>
    public required string Provider { get; init; }

    /// <summary>The SHA-256 hash of the subject, in upper-case hex.</summary>
    public required string ProviderKeyHash { get; init; }

    /// <summary>The provider's userinfo as it arrived, best effort.</summary>
    public string? ClaimsJson { get; init; }

    public DateTimeOffset CreatedUtc { get; init; } = DateTimeOffset.UtcNow;
}
