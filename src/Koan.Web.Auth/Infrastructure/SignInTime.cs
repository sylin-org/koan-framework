using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Web.Auth.Infrastructure;

/// <summary>
/// When a cookie's person signed in. The cookie's own issue time moves with every sliding renewal, so Koan stamps the
/// sign-in in the ticket's items, which a renewal keeps.
/// </summary>
internal static class SignInTime
{
    /// <summary>Stamps now on a cookie being signed in.</summary>
    public static void Stamp(HttpContext context, AuthenticationProperties properties)
        => properties.Items[AuthConstants.Items.SignedIn] = Now(context).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);

    /// <summary>Whether the cookie read with <paramref name="properties"/> signed in no longer than <paramref name="maxAge"/> ago.</summary>
    public static bool Within(HttpContext context, AuthenticationProperties? properties, TimeSpan maxAge)
    {
        if (properties?.Items.TryGetValue(AuthConstants.Items.SignedIn, out var stamped) != true
            || !long.TryParse(stamped, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            return false;
        return Now(context) - DateTimeOffset.FromUnixTimeSeconds(seconds) <= maxAge;
    }

    private static DateTimeOffset Now(HttpContext context)
        => (context.RequestServices.GetService<TimeProvider>() ?? TimeProvider.System).GetUtcNow();
}
