using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Web.Auth.TestProvider.Infrastructure;
using Sora.Web.Auth.TestProvider.Options;
using System.Web;

namespace Sora.Web.Auth.TestProvider.Controllers;

[ApiController]
public sealed class AuthorizeController(IOptionsSnapshot<TestProviderOptions> opts, DevTokenStore store, IHostEnvironment env, ILogger<AuthorizeController> logger) : ControllerBase
{
    [HttpGet]
    [Route(".testoauth/authorize")]
    public IActionResult Authorize([FromQuery] string response_type, [FromQuery] string client_id, [FromQuery] string redirect_uri, [FromQuery] string? scope, [FromQuery] string? state, [FromQuery] string? code_challenge, [FromQuery] string? code_challenge_method, [FromQuery] string? prompt)
    {
        var o = opts.Value;
        if (!(env.IsDevelopment() || o.Enabled)) return NotFound();
        if (string.IsNullOrWhiteSpace(response_type) || !string.Equals(response_type, "code", StringComparison.OrdinalIgnoreCase)) return BadRequest("response_type must be 'code'");
        if (string.IsNullOrWhiteSpace(client_id) || string.IsNullOrWhiteSpace(redirect_uri)) return BadRequest("client_id and redirect_uri are required");
        if (!string.Equals(client_id, o.ClientId, StringComparison.Ordinal)) return Unauthorized();

    // If prompt requests a fresh login/selection, ignore any existing cookie and show the login UI.
    var forceLogin = !string.IsNullOrWhiteSpace(prompt) && (string.Equals(prompt, "login", StringComparison.OrdinalIgnoreCase) || string.Equals(prompt, "select_account", StringComparison.OrdinalIgnoreCase));
    if (forceLogin)
    {
      try { Response.Cookies.Append("_tp_user", string.Empty, new CookieOptions { Expires = DateTimeOffset.UnixEpoch, Path = "/", SameSite = SameSiteMode.Lax, HttpOnly = false, Secure = Request.IsHttps }); } catch { /* ignore */ }
    }

    // Render simple HTML form when no user cookie or when forced via prompt.
    if (forceLogin || !Request.Cookies.TryGetValue("_tp_user", out var userCookie) || string.IsNullOrWhiteSpace(userCookie))
        {
      // Serve a dedicated static HTML to keep SoC clean
            var url = o.RouteBase.TrimEnd('/') + "/login.html";
            var qlogin = HttpUtility.ParseQueryString(string.Empty);
            qlogin["client_id"] = client_id;
            qlogin["redirect_uri"] = redirect_uri;
            if (!string.IsNullOrWhiteSpace(scope)) qlogin["scope"] = scope;
            if (!string.IsNullOrWhiteSpace(state)) qlogin["state"] = state;
            if (!string.IsNullOrWhiteSpace(code_challenge)) qlogin["code_challenge"] = code_challenge;
            if (!string.IsNullOrWhiteSpace(code_challenge_method)) qlogin["code_challenge_method"] = code_challenge_method;
            var sep = url.Contains('?') ? "&" : "?";
            return Redirect(url + sep + qlogin.ToString());
        }

        // At this point userCookie is present and non-empty.
        var decoded = Uri.UnescapeDataString(userCookie ?? string.Empty);
        var parts = decoded.Split('|');
        var profile = new UserProfile(parts.ElementAtOrDefault(0) ?? "dev", parts.ElementAtOrDefault(1) ?? "dev@example.com", null);
    // Parse roles/perms/claims from query
    var (roles, perms, extraClaims) = ParseExtras(o);
    var code = store.IssueCode(profile, TimeSpan.FromMinutes(5), code_challenge, roles, perms, extraClaims);
        var uri = new UriBuilder(redirect_uri);
        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
        q["code"] = code; if (!string.IsNullOrWhiteSpace(state)) q["state"] = state;
        uri.Query = q.ToString()!;
        logger.LogDebug("TestProvider authorize: issuing code and redirecting to {Redirect}", uri.ToString());
        return Redirect(uri.ToString());
    }

  private (ISet<string> Roles, ISet<string> Permissions, IDictionary<string, string[]> Claims) ParseExtras(TestProviderOptions o)
  {
    var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var perms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var claims = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    var query = HttpUtility.ParseQueryString(Request.QueryString.Value ?? string.Empty);
    void takeCsv(string? csv, ISet<string> set, int cap)
    {
      if (string.IsNullOrWhiteSpace(csv)) return;
      foreach (var raw in csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
      {
        if (set.Count >= cap) break;
        set.Add(raw);
      }
    }

    takeCsv(query.Get("roles") ?? query.Get("sora.roles"), roles, o.MaxRoles);
    takeCsv(query.Get("perms") ?? query.Get("permissions") ?? query.Get("sora.permissions"), perms, o.MaxPermissions);

    foreach (var key in query.AllKeys ?? Array.Empty<string>())
    {
      if (string.IsNullOrWhiteSpace(key)) continue;
      if (!key.StartsWith("claim.", StringComparison.OrdinalIgnoreCase)) continue;
      var type = key.Substring("claim.".Length);
      if (string.IsNullOrWhiteSpace(type)) continue;
      if (!claims.TryGetValue(type, out var list)) { if (claims.Count >= o.MaxCustomClaimTypes) break; claims[type] = list = new List<string>(); }
      var vals = query.GetValues(key) ?? Array.Empty<string>();
      foreach (var v in vals)
      {
        if (string.IsNullOrWhiteSpace(v)) continue;
        foreach (var iv in v.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
          if (list.Count >= o.MaxValuesPerClaimType) break;
          if (!list.Contains(iv)) list.Add(iv);
        }
      }
    }

    var normalized = claims.ToDictionary(k => k.Key, v => v.Value.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), StringComparer.OrdinalIgnoreCase);
    return (roles, perms, normalized);
  }
}
