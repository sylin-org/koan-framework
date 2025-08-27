using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Web.Auth.TestProvider.Infrastructure;
using Sora.Web.Auth.TestProvider.Options;

namespace Sora.Web.Auth.TestProvider.Controllers;

[ApiController]
public sealed class TokenController(IOptionsSnapshot<TestProviderOptions> opts, DevTokenStore store, IHostEnvironment env, ILogger<TokenController> logger) : ControllerBase
{
    public sealed record TokenRequest(string grant_type, string code, string redirect_uri, string client_id, string? client_secret, string? code_verifier);

    [HttpPost]
    [Route(".testoauth/token")]
    public IActionResult Token([FromForm] TokenRequest req)
    {
        var o = opts.Value;
        if (!(env.IsDevelopment() || o.Enabled)) return NotFound();
        if (!string.Equals(req.grant_type, "authorization_code", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { error = "unsupported_grant_type" });
        if (string.IsNullOrWhiteSpace(req.code) || string.IsNullOrWhiteSpace(req.redirect_uri) || string.IsNullOrWhiteSpace(req.client_id)) return BadRequest(new { error = "invalid_request" });
        if (!string.Equals(req.client_id, o.ClientId, StringComparison.Ordinal) || !string.Equals(req.client_secret ?? string.Empty, o.ClientSecret, StringComparison.Ordinal)) return Unauthorized();

        if (!store.TryRedeemCode(req.code, out var profile, out var challenge)) { logger.LogDebug("TestProvider token: invalid_grant for code {Code}", req.code); return BadRequest(new { error = "invalid_grant" }); }
        // v1: accept PKCE params but do not enforce; future: verify code_verifier against challenge
        var token = store.IssueToken(profile, TimeSpan.FromHours(1));
        logger.LogDebug("TestProvider token: issued access token for {Email}", profile.Email);
        return Ok(new { access_token = token, token_type = "Bearer", expires_in = 3600 });
    }
}
