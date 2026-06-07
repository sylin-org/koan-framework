using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Koan.Security.Trust.Dev;

/// <summary>
/// SEC-0001 §4 (Rung 0) — zero-config dev identity. When a request is otherwise unauthenticated, fills in
/// a dev principal so <c>[Authorize]</c> and <see cref="Identity"/> just work with no OAuth dance. Persona
/// testing: <c>?_as=&lt;sub&gt;&amp;_roles=a,b</c>; stay unauthenticated with <c>?_as=anonymous</c>.
/// <para>
/// The auth pipeline (<c>KoanWebAuthStartupFilter</c>) inserts this BETWEEN authentication and authorization,
/// and ONLY in Development — so it is never present in a production pipeline (the §4.2 fail-closed invariant).
/// </para>
/// </summary>
public sealed class KoanDevIdentityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly DevIdentityOptions _options;

    public KoanDevIdentityMiddleware(RequestDelegate next, IOptions<DevIdentityOptions> options)
    {
        _next = next;
        _options = options.Value;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (_options.Enabled && context.User?.Identity?.IsAuthenticated != true)
        {
            var asParam = context.Request.Query["_as"].ToString();
            // ?_as=anonymous opts out (test the unauthenticated path); otherwise auto-sign-in.
            if (!string.Equals(asParam, "anonymous", StringComparison.OrdinalIgnoreCase))
            {
                var subject = string.IsNullOrWhiteSpace(asParam) ? _options.Subject : asParam;
                var rolesParam = context.Request.Query["_roles"].ToString();
                var roles = string.IsNullOrWhiteSpace(rolesParam)
                    ? _options.Roles
                    : rolesParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                context.User = BuildPrincipal(subject, roles);
            }
        }

        return _next(context);
    }

    private static ClaimsPrincipal BuildPrincipal(string subject, string[] roles)
    {
        var claims = new List<Claim>
        {
            new("sub", subject),
            new(ClaimTypes.Name, subject),
        };
        foreach (var role in roles) claims.Add(new Claim(ClaimTypes.Role, role));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Koan.dev"));
    }
}

/// <summary>Pipeline insertion for the zero-config dev identity (Development-only — see middleware remarks).</summary>
public static class DevIdentityApplicationBuilderExtensions
{
    public static IApplicationBuilder UseKoanDevIdentity(this IApplicationBuilder app)
        => app.UseMiddleware<KoanDevIdentityMiddleware>();
}
