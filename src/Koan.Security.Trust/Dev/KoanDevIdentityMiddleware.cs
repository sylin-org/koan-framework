using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Koan.Security.Trust.Dev;

/// <summary>
/// SEC-0001 §4 / SEC-0003 §2.3 — the <c>?_as=</c> dev persona override. <b>The default is anonymous</b>:
/// a request is given a (transient, cookie-less) principal ONLY when <c>?_as=&lt;subject&gt;</c> is explicitly
/// provided — for scripted testing of different users/setups (<c>?_as=alice&amp;_roles=editor</c>). Omitting
/// <c>?_as</c>, or <c>?_as=anonymous</c>, stays unauthenticated so the public interface is what you see by
/// default. The everyday dev *login* is the TestProvider page, which mints a real session.
/// <para>
/// The auth pipeline inserts this BETWEEN authentication and authorization, and ONLY in Development (via the
/// WEB-0069 contributor) — so it is never present in a production pipeline (the §4.2 fail-closed invariant).
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
            // SEC-0003 §2.3 — DEFAULT ANONYMOUS. Impersonate only when ?_as=<subject> is explicitly given and
            // is not 'anonymous'. (No ?_as ⇒ do nothing ⇒ the request stays unauthenticated / public.)
            if (!string.IsNullOrWhiteSpace(asParam) && !string.Equals(asParam, "anonymous", StringComparison.OrdinalIgnoreCase))
            {
                var rolesParam = context.Request.Query["_roles"].ToString();
                var roles = string.IsNullOrWhiteSpace(rolesParam)
                    ? _options.Roles
                    : rolesParam.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                context.User = BuildPrincipal(asParam, roles);
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
