using System.Security.Claims;

namespace S7.TechDocs.Infrastructure;

/// <summary>
/// Development-only middleware to enrich the authenticated principal (from TestProvider)
/// with role claims based on a simple cookie. This keeps identity issuance with Koan.Web.Auth
/// while allowing us to simulate Roles for the demo.
/// Cookie format: _s7_roles=Reader|Author|Moderator|Admin (pipe or comma separated). Defaults to Reader.
/// Cumulative semantics are applied: Reader ⊆ Author ⊆ Moderator ⊆ Admin.
/// </summary>
public sealed class DevRoleClaimsMiddleware(RequestDelegate next, IWebHostEnvironment env)
{
    public async Task Invoke(HttpContext context)
    {
        if (env.IsDevelopment())
        {
            var user = context.User;
            if (user?.Identity?.IsAuthenticated == true)
            {
                var rolesCookie = context.Request.Cookies.TryGetValue("_s7_roles", out var v) ? v : null;
                var roles = ParseRoles(rolesCookie);
                if (roles.Count > 0 && !HasAnyRole(user))
                {
                    var identity = user.Identity as ClaimsIdentity;
                    foreach (var r in ExpandCumulative(roles))
                    {
                        identity!.AddClaim(new Claim(ClaimTypes.Role, r));
                    }
                }
            }
        }

        await next(context);
    }

    private static HashSet<string> ParseRoles(string? raw)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(raw)) return set;
        foreach (var part in raw.Split(new[] { '|', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            set.Add(part);
        }
        return set;
    }

    private static bool HasAnyRole(ClaimsPrincipal user)
        => user.Claims.Any(c => c.Type == ClaimTypes.Role);

    private static IEnumerable<string> ExpandCumulative(HashSet<string> input)
    {
        // Cumulative: Admin -> Moderator -> Author -> Reader
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
