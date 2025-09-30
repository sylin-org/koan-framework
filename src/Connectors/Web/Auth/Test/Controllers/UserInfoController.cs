using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Koan.Web.Auth.Connector.Test.Infrastructure;

namespace Koan.Web.Auth.Connector.Test.Controllers;

public sealed class UserInfoController(DevTokenStore store, IHostEnvironment env, ILogger<UserInfoController> logger) : ControllerBase
{
    [HttpGet]
    public IActionResult UserInfo()
    {
        if (!env.IsDevelopment()) return NotFound(); // extra guard
        var auth = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(auth) || !auth.StartsWith("Bearer ", StringComparison.Ordinal)) return Unauthorized();
        var token = auth.Substring("Bearer ".Length).Trim();
    if (!store.TryGetToken(token, out var profile, out var envx)) { logger.LogDebug("TestProvider userinfo: invalid token"); return Unauthorized(); }
    logger.LogDebug("TestProvider userinfo: returning profile for {Email}", profile.Email);
    // Build claims payload
    var roles = envx.Roles.Count > 0 ? envx.Roles.ToArray() : Array.Empty<string>();
    var perms = envx.Permissions.Count > 0 ? envx.Permissions.ToArray() : Array.Empty<string>();
    var claims = envx.Claims.Count > 0 ? envx.Claims.ToDictionary(k => k.Key, v => v.Value.Count == 1 ? (object)v.Value[0] : (object)v.Value.ToArray()) : new Dictionary<string, object>();
    return Ok(new { id = profile.Email, username = profile.Username, email = profile.Email, roles, permissions = perms, claims });
    }
}

