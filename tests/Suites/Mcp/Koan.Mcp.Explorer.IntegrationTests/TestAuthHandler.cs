using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Mcp.Explorer.IntegrationTests;

/// <summary>
/// The default authenticate scheme for the test host: a request carrying <c>X-Test-Auth</c> (e.g.
/// <c>"role=admin;scope=docs:read"</c>) becomes an authenticated principal with those roles/scopes; absent → anonymous.
/// Stands in for the cookie session so the Explorer's <c>context.User</c> is a real principal the gate evaluates.
/// </summary>
internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";
    public const string HeaderName = "X-Test-Auth";

    public TestAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-user"),
            new("sub", "test-user"),
        };
        var scopes = new List<string>();
        foreach (var part in raw.ToString().Split(';', System.StringSplitOptions.RemoveEmptyEntries | System.StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2) continue;
            var key = kv[0].Trim().ToLowerInvariant();
            var value = kv[1].Trim();
            if (key == "role") claims.Add(new Claim(ClaimTypes.Role, value));
            else if (key == "scope") scopes.Add(value);
        }
        if (scopes.Count > 0) claims.Add(new Claim("scope", string.Join(' ', scopes)));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
