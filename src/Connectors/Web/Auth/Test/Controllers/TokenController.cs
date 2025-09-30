using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Web.Auth.Connector.Test.Infrastructure;
using Koan.Web.Auth.Connector.Test.Options;
using System.Security.Cryptography;
using System.Text;

namespace Koan.Web.Auth.Connector.Test.Controllers;

public sealed class TokenController(IOptionsSnapshot<TestProviderOptions> opts, DevTokenStore store, IHostEnvironment env, ILogger<TokenController> logger) : ControllerBase
{
    public sealed record TokenRequest(string grant_type, string? code, string? redirect_uri, string client_id, string? client_secret, string? code_verifier, string? scope);

    [HttpPost]
    public IActionResult Token([FromForm] TokenRequest req)
    {
        var o = opts.Value;
        if (!(env.IsDevelopment() || o.Enabled)) return NotFound();

        // Handle client credentials flow
        if (string.Equals(req.grant_type, "client_credentials", StringComparison.OrdinalIgnoreCase))
        {
            return HandleClientCredentials(req, o);
        }

        // Handle authorization code flow (existing logic)
        if (!string.Equals(req.grant_type, "authorization_code", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { error = "unsupported_grant_type" });
        if (string.IsNullOrWhiteSpace(req.code) || string.IsNullOrWhiteSpace(req.redirect_uri) || string.IsNullOrWhiteSpace(req.client_id)) return BadRequest(new { error = "invalid_request" });
        if (!string.Equals(req.client_id, o.ClientId, StringComparison.Ordinal) || !string.Equals(req.client_secret ?? string.Empty, o.ClientSecret, StringComparison.Ordinal)) return Unauthorized();

        if (!store.TryRedeemCode(req.code, out var profile, out var challenge, out var envx)) { logger.LogDebug("TestProvider token: invalid_grant for code {Code}", req.code); return BadRequest(new { error = "invalid_grant" }); }
        // Enforce PKCE S256 when a challenge is present
        if (!string.IsNullOrWhiteSpace(challenge))
        {
            if (string.IsNullOrWhiteSpace(req.code_verifier)) { logger.LogDebug("TestProvider token: missing code_verifier for code {Code}", req.code); return BadRequest(new { error = "invalid_grant" }); }
            var verifierBytes = Encoding.UTF8.GetBytes(req.code_verifier);
            var hash = SHA256.HashData(verifierBytes);
            var computed = Base64UrlEncode(hash);
            if (!string.Equals(computed, challenge, StringComparison.Ordinal))
            {
                logger.LogDebug("TestProvider token: PKCE verification failed for code {Code}", req.code);
                return BadRequest(new { error = "invalid_grant" });
            }
        }
        var token = store.IssueToken(profile, TimeSpan.FromHours(1), envx);
        logger.LogDebug("TestProvider token: issued access token for {Email}", profile.Email);
        return Ok(new { access_token = token, token_type = "Bearer", expires_in = 3600 });
    }

    private IActionResult HandleClientCredentials(TokenRequest req, TestProviderOptions options)
    {
        if (!options.EnableClientCredentials)
        {
            logger.LogDebug("TestProvider token: client_credentials flow is disabled");
            return BadRequest(new { error = "unsupported_grant_type" });
        }

        // Validate client credentials
        if (string.IsNullOrWhiteSpace(req.client_id) || string.IsNullOrWhiteSpace(req.client_secret))
        {
            logger.LogDebug("TestProvider token: missing client credentials");
            return BadRequest(new { error = "invalid_request" });
        }

        if (!options.RegisteredClients.TryGetValue(req.client_id, out var client) ||
            !string.Equals(client.ClientSecret, req.client_secret, StringComparison.Ordinal))
        {
            logger.LogDebug("TestProvider token: invalid client credentials for {ClientId}", req.client_id);
            return Unauthorized(new { error = "invalid_client" });
        }

        // Parse and validate scopes
        var requestedScopes = ParseScopes(req.scope);
        var allowedScopes = client.AllowedScopes.Intersect(options.AllowedScopes).ToArray();
        var grantedScopes = requestedScopes.Intersect(allowedScopes).ToArray();

        // Create service profile
        var serviceProfile = new UserProfile(req.client_id, $"{req.client_id}@service", null);
        var claimEnv = new DevTokenStore.ClaimEnvelope();

        // Add scopes as permissions
        foreach (var scope in grantedScopes)
            claimEnv.Permissions.Add(scope);

        // Add client-specific claims
        claimEnv.Claims["client_id"] = new List<string> { req.client_id };
        claimEnv.Claims["aud"] = new List<string> { options.JwtAudience };
        claimEnv.Claims["token_type"] = new List<string> { "service" };

        // Issue token
        var token = store.IssueToken(serviceProfile, TimeSpan.FromHours(1), claimEnv);

        logger.LogDebug("TestProvider token: issued service token for client {ClientId} with scopes {Scopes}",
            req.client_id, string.Join(",", grantedScopes));

        return Ok(new
        {
            access_token = token,
            token_type = "Bearer",
            expires_in = 3600,
            scope = string.Join(' ', grantedScopes)
        });
    }

    private string[] ParseScopes(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return Array.Empty<string>();

        return scope.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> data)
    {
        var s = Convert.ToBase64String(data);
        // Make URL safe and trim padding per RFC 7636
        return s.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}

