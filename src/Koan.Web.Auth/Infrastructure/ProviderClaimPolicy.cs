using System.Security.Claims;
using Koan.Web.Auth.Domain;
using Koan.Web.Auth.Providers;

namespace Koan.Web.Auth.Infrastructure;

/// <summary>
/// The one chokepoint between what a provider asserted and what the cookie carries, for every protocol: it runs when
/// a provider's sign-in reaches the cookie, before any flow handler. It stamps the provider-qualified subject, drops
/// every role the provider asserted unless that provider opts in (<see cref="Options.ProviderOptions.MapRolesAndClaims"/>),
/// and always drops Koan's reserved roles. An OIDC id_token's <c>roles</c> become role claims before Koan sees them, so
/// mapping userinfo selectively would not be enough. A sign-in no provider made, such as an application's own
/// <c>SignInAsync</c>, is the application's statement and passes untouched.
/// </summary>
internal static class ProviderClaimPolicy
{
    public static void Apply(ClaimsIdentity identity, string? provider, AuthProviderPlan? plan)
    {
        if (provider is null || plan is null) return;
        var route = plan.Routes.FirstOrDefault(candidate => string.Equals(candidate.Info.Id, provider, StringComparison.OrdinalIgnoreCase));
        if (route is null) return;

        foreach (var forged in identity.FindAll(SignInProof.ProviderSubjectClaim).ToArray())
            identity.TryRemoveClaim(forged);
        var subject = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? identity.FindFirst("sub")?.Value;
        if (!string.IsNullOrWhiteSpace(subject))
            identity.AddClaim(new Claim(SignInProof.ProviderSubjectClaim, SignInProof.ProviderSubject(route.Info.Id, subject)));

        var mapsRoles = route.Options.MapRolesAndClaims == true;
        foreach (var role in identity.Claims.Where(claim => IsRoleClaim(identity, claim)).ToArray())
            if (!mapsRoles || SignInProof.IsReservedRole(role.Value))
                identity.TryRemoveClaim(role);
    }

    private static bool IsRoleClaim(ClaimsIdentity identity, Claim claim)
        => claim.Type is ClaimTypes.Role or "role" or "roles" || claim.Type == identity.RoleClaimType;
}
