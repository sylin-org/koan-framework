using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Web.Auth.TestProvider.Infrastructure;
using Sora.Web.Auth.TestProvider.Options;
using System.Security.Cryptography;
using System.Text;

namespace Sora.Web.Auth.TestProvider.Controllers;

public sealed class TokenController(IOptionsSnapshot<TestProviderOptions> opts, DevTokenStore store, IHostEnvironment env, ILogger<TokenController> logger) : ControllerBase
{
    public sealed record TokenRequest(string grant_type, string code, string redirect_uri, string client_id, string? client_secret, string? code_verifier);

    [HttpPost]
    public IActionResult Token([FromForm] TokenRequest req)
    {
        var o = opts.Value;
        if (!(env.IsDevelopment() || o.Enabled)) return NotFound();
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

    private static string Base64UrlEncode(ReadOnlySpan<byte> data)
    {
        var s = Convert.ToBase64String(data);
        // Make URL safe and trim padding per RFC 7636
        return s.Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
