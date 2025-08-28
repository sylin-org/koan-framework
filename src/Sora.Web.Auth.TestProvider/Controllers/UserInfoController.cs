using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sora.Web.Auth.TestProvider.Infrastructure;

namespace Sora.Web.Auth.TestProvider.Controllers;

[ApiController]
public sealed class UserInfoController(DevTokenStore store, IHostEnvironment env, ILogger<UserInfoController> logger) : ControllerBase
{
    [HttpGet]
    [Route(".testoauth/userinfo")]
    public IActionResult UserInfo()
    {
        if (!env.IsDevelopment()) return NotFound(); // extra guard
        var auth = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer ", StringComparison.Ordinal)) return Unauthorized();
        var token = auth.Substring("Bearer ".Length).Trim();
        if (!store.TryGetProfile(token, out var profile)) { logger.LogDebug("TestProvider userinfo: invalid token"); return Unauthorized(); }
        logger.LogDebug("TestProvider userinfo: returning profile for {Email}", profile.Email);
        return Ok(new { id = profile.Email, username = profile.Username, email = profile.Email });
    }
}
