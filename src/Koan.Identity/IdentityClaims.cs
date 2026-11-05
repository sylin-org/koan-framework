namespace Koan.Identity;

/// <summary>
/// The minimal claim bag the reconciler upserts an <see cref="Identity"/> from. Extracted once at the sign-in
/// chokepoint (cookie claims or provider userinfo) and threaded down — never re-read from ambient state.
/// </summary>
/// <param name="Subject">The stable subject (claims <c>sub</c> / NameIdentifier): the provider's, or, with no provider, the person's id.</param>
/// <param name="DisplayName">Best-effort display name from the IdP.</param>
/// <param name="Picture">Best-effort avatar/picture URL.</param>
/// <param name="Email">Best-effort email; when present, reconciled as an <see cref="IdentityEmail"/> factor.</param>
/// <param name="EmailVerified">Whether the IdP asserted the email is verified.</param>
/// <param name="Provider">The provider that vouched for <paramref name="Subject"/>; none for the application's own sign-in.</param>
/// <param name="ClaimsJson">The provider's userinfo as it arrived, kept on the login.</param>
public sealed record IdentityClaims(
    string Subject,
    string? DisplayName = null,
    string? Picture = null,
    string? Email = null,
    bool EmailVerified = false,
    string? Provider = null,
    string? ClaimsJson = null);
