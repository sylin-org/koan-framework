using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Koan.Security.Trust.Infrastructure;

namespace Koan.Security.Trust.Dev;

/// <summary>
/// Builds the explicit Development persona selected by <c>?_as=</c>. The default is anonymous; a real authenticated
/// principal is never replaced. Web Auth contributes the returned principal through Koan's ordered Web context.
/// </summary>
public static class DevIdentity
{
    /// <summary>Return the requested Development principal, or <c>null</c> when the request remains unchanged.</summary>
    public static ClaimsPrincipal? Resolve(HttpContext context, DevIdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.Enabled || context.User?.Identity?.IsAuthenticated == true) return null;

        var subject = context.Request.Query[Constants.DevIdentity.SubjectQuery].ToString();
        if (string.IsNullOrWhiteSpace(subject)
            || string.Equals(subject, Constants.DevIdentity.Anonymous, StringComparison.OrdinalIgnoreCase))
            return null;

        var rolesValue = context.Request.Query[Constants.DevIdentity.RolesQuery].ToString();
        var roles = string.IsNullOrWhiteSpace(rolesValue)
            ? options.Roles
            : rolesValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var claims = new List<Claim>
        {
            new(Constants.DevIdentity.SubjectClaim, subject),
            new(ClaimTypes.Name, subject),
        };
        foreach (var role in roles) claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: Constants.DevIdentity.AuthenticationType));
    }
}
