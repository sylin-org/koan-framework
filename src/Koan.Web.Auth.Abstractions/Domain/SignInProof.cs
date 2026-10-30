using System.Security.Claims;

namespace Koan.Web.Auth.Domain;

/// <summary>
/// What a sign-in proved about a person, as distinct from what they may have typed into a profile. A display name proves
/// nothing, an unverified email proves nothing, and a bare <c>sub</c> means nothing outside the provider that issued it.
/// Access that a configured list grants keys on these, and on nothing else. Koan's reserved roles are never a provider's
/// to assert.
/// </summary>
public static class SignInProof
{
    /// <summary>
    /// The claim carrying <c>{provider}:{subject}</c>, stamped by Web Auth at a provider's sign-in, before anything
    /// rewrites the subject. Whatever a provider asserted under this type is replaced.
    /// </summary>
    public const string ProviderSubjectClaim = "koan_provider_subject";

    /// <summary>The standard OIDC claim saying the provider verified the email it asserted.</summary>
    public const string EmailVerifiedClaim = "email_verified";

    private const string ReservedRolePrefix = "koan:";

    /// <summary>The provider-qualified form of a subject, the one a configured allow-list names.</summary>
    public static string ProviderSubject(string provider, string subject) => $"{provider}:{subject}";

    /// <summary>The provider-qualified subject <paramref name="principal"/> signed in as, or null when no provider signed them in.</summary>
    public static string? ProviderSubjectOf(ClaimsPrincipal principal) => principal.FindFirst(ProviderSubjectClaim)?.Value;

    /// <summary>The emails <paramref name="identity"/> carries when its provider verified them; none otherwise.</summary>
    public static IReadOnlyList<string> VerifiedEmails(ClaimsIdentity identity) => VerifiedEmails(identity.Claims);

    /// <summary>The emails <paramref name="principal"/> carries when its provider verified them; none otherwise.</summary>
    public static IReadOnlyList<string> VerifiedEmails(ClaimsPrincipal principal) => VerifiedEmails(principal.Claims);

    /// <summary>True for a role Koan reserves (<c>koan:*</c>), which only Koan's own role state or an operator's configuration grants.</summary>
    public static bool IsReservedRole(string? role)
        => role?.Trim().StartsWith(ReservedRolePrefix, StringComparison.OrdinalIgnoreCase) == true;

    private static IReadOnlyList<string> VerifiedEmails(IEnumerable<Claim> claims)
    {
        var all = claims as ICollection<Claim> ?? claims.ToList();
        if (!all.Any(static claim => claim.Type == EmailVerifiedClaim && string.Equals(claim.Value, "true", StringComparison.OrdinalIgnoreCase)))
            return [];
        return all.Where(static claim => claim.Type is ClaimTypes.Email or "email" && !string.IsNullOrWhiteSpace(claim.Value))
            .Select(static claim => claim.Value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
