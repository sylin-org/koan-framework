using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Web.Auth.TestProvider.Infrastructure;
using Sora.Web.Auth.TestProvider.Options;

namespace Sora.Web.Auth.TestProvider.Controllers;

[ApiController]
public sealed class AuthorizeController(IOptionsSnapshot<TestProviderOptions> opts, DevTokenStore store, IHostEnvironment env) : ControllerBase
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

  // Render simple HTML form when no user cookie (prompt=login just ensures the prompt appears in that case).
  var hasCookie = Request.Cookies.TryGetValue("_tp_user", out var userCookie) && !string.IsNullOrWhiteSpace(userCookie);
  if (!hasCookie)
        {
            var html = $$"""
<!doctype html>
<html><head><meta charset="utf-8"><title>Test OAuth — Consent</title></head>
<body>
  <h3>Test OAuth — Sign in</h3>
  <form id="f">
    <label>Name <input id="u" /></label><br/>
    <label>Email <input id="e" type="email" /></label><br/>
    <button type="submit">Continue</button>
  </form>
  <script>
    const u = document.getElementById('u');
    const e = document.getElementById('e');
    u.value = localStorage.getItem('tp_u')||'';
    e.value = localStorage.getItem('tp_e')||'';
    document.getElementById('f').addEventListener('submit', ev => {
      ev.preventDefault();
      localStorage.setItem('tp_u', u.value);
      localStorage.setItem('tp_e', e.value);
      document.cookie = `_tp_user=${encodeURIComponent(u.value)}|${encodeURIComponent(e.value)}; path=/`;
      location.reload();
    });
  </script>
  <noscript>Enable JavaScript.</noscript>
</body></html>
""";
      return new ContentResult { ContentType = "text/html", Content = html };
        }

    var parts = Uri.UnescapeDataString(userCookie).Split('|');
        var profile = new UserProfile(parts.ElementAtOrDefault(0) ?? "dev", parts.ElementAtOrDefault(1) ?? "dev@example.com", null);
        var code = store.IssueCode(profile, TimeSpan.FromMinutes(5), code_challenge);
        var uri = new UriBuilder(redirect_uri);
        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
        q["code"] = code; if (!string.IsNullOrWhiteSpace(state)) q["state"] = state;
        uri.Query = q.ToString()!;
        return Redirect(uri.ToString());
    }
}
