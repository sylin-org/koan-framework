using System.Security.Claims;

namespace Koan.Web.Auth.Domain;

/// <summary>Where a login finds its person: Web Auth hands every verified login here at its callback.</summary>
public interface IExternalIdentityStore
{
    /// <summary>The logins the person <paramref name="userId"/> holds.</summary>
    Task<IReadOnlyList<ExternalIdentity>> GetByUser(string userId, CancellationToken ct = default);

    /// <summary>
    /// A sign-in with <paramref name="identity"/> arrived: it signs in as the person the login is bound to, and a login
    /// seen for the first time is bound to a new person.
    /// </summary>
    Task Link(ExternalIdentity identity, CancellationToken ct = default);

    /// <summary>
    /// Binds the verified <paramref name="identity"/> to the person <paramref name="signedIn"/> is, in place of any login
    /// of the same provider they held. A login that is another person's stays theirs: nothing merges.
    /// </summary>
    Task<BindOutcome> Bind(ClaimsPrincipal signedIn, ExternalIdentity identity, CancellationToken ct = default);
}
