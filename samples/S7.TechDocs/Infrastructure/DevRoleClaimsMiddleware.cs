using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace S7.TechDocs.Infrastructure;

/// <summary>
/// Parses the development role cookie and enriches the authenticated principal.
/// </summary>
public static class DevRoleClaims
{
    private const string RolesCookie = "_s7_roles";
    private static readonly char[] Delimiters = ['|', ',', ';'];

    public static Task<ClaimsPrincipal> ApplyAsync(IServiceProvider services, ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true || HasAnyRole(principal))
        {
            return Task.FromResult(principal);
        }

        var accessor = services.GetRequiredService<IHttpContextAccessor>();
        var context = accessor.HttpContext;
        if (context is null)
        {
            return Task.FromResult(principal);
        }

        var rolesCookie = context.Request.Cookies.TryGetValue(RolesCookie, out var value) ? value : null;
        var roles = ParseRoles(rolesCookie);
        if (roles.Count == 0)
        {
            return Task.FromResult(principal);
        }

        var identity = principal.Identity as ClaimsIdentity ?? new ClaimsIdentity(principal.Identity);
        if (identity != principal.Identity)
        {
            principal.AddIdentity(identity);
        }

        foreach (var role in ExpandCumulative(roles))
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, role));
        }

        return Task.FromResult(principal);
    }

    private static HashSet<string> ParseRoles(string? raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return set;
        foreach (var part in raw.Split(Delimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(part);
        }
        return set;
    }

    private static bool HasAnyRole(ClaimsPrincipal user)
        => user.Claims.Any(c => c.Type == ClaimTypes.Role);

    private static IEnumerable<string> ExpandCumulative(HashSet<string> input)
    {
        if (input.Contains(Constants.Roles.Admin))
        {
            yield return Constants.Roles.Admin;
            yield return Constants.Roles.Moderator;
            yield return Constants.Roles.Author;
            yield return Constants.Roles.Reader;
            yield break;
        }
        if (input.Contains(Constants.Roles.Moderator))
        {
            yield return Constants.Roles.Moderator;
            yield return Constants.Roles.Author;
            yield return Constants.Roles.Reader;
            yield break;
        }
        if (input.Contains(Constants.Roles.Author))
        {
            yield return Constants.Roles.Author;
            yield return Constants.Roles.Reader;
            yield break;
        }
        if (input.Contains(Constants.Roles.Reader))
        {
            yield return Constants.Roles.Reader;
        }
    }
}
