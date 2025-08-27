using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sora.Web.Auth.TestProvider.Infrastructure;
using Sora.Web.Auth.TestProvider.Options;

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

        // Render simple HTML form when no user cookie (prompt=login just ensures the prompt appears in that case).
        if (!Request.Cookies.TryGetValue("_tp_user", out var userCookie) || string.IsNullOrWhiteSpace(userCookie))
        {
            var html = $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Sora TestProvider — Sign in</title>
  <script src="https://cdn.tailwindcss.com"></script>
  <link href="https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.0.0/css/all.min.css" rel="stylesheet">
  <link href="/styles.css" rel="stylesheet">
  <script>tailwind.config = { theme: { extend: { colors: { slate: { 950: '#020617' } } } } };</script>
  <style>
    .card { background: linear-gradient(180deg, rgba(15,23,42,1) 0%, rgba(2,6,23,1) 100%); }
  </style>
  </head>
<body class="bg-slate-950 text-white min-h-screen flex flex-col">
  <header class="bg-slate-900 border-b border-slate-800">
    <div class="max-w-3xl mx-auto px-4 sm:px-6 lg:px-8">
      <div class="flex items-center justify-between h-16">
        <a href="/" class="flex items-center space-x-3" title="Home">
          <div class="w-8 h-8 bg-gradient-to-r from-purple-500 to-pink-500 rounded-lg flex items-center justify-center">
            <i class="fas fa-satellite-dish text-white text-sm"></i>
          </div>
          <h1 class="text-lg font-bold bg-gradient-to-r from-purple-400 to-pink-400 bg-clip-text text-transparent">Sora — Sign in</h1>
        </a>
      </div>
    </div>
  </header>

  <main class="flex-1 flex items-center justify-center px-4 py-10">
    <div class="w-full max-w-md card rounded-2xl border border-slate-800 shadow-2xl p-6">
      <div class="flex items-center gap-3 mb-4">
        <div class="w-10 h-10 bg-gradient-to-r from-purple-500 to-pink-500 rounded-xl flex items-center justify-center">
          <i class="fa-regular fa-user text-white"></i>
        </div>
        <div>
          <div class="text-xl font-semibold">Sign in</div>
          <div class="text-sm text-gray-400">Development Test Provider</div>
        </div>
      </div>

      <form id="f" class="space-y-4">
        <div>
          <label for="u" class="block text-sm text-gray-300 mb-1">Name</label>
          <input id="u" class="w-full bg-slate-900 text-white rounded-lg px-3 py-2 border border-slate-700 focus:outline-none focus:ring-2 focus:ring-purple-500" placeholder="Jane Doe" />
        </div>
        <div>
          <label for="e" class="block text-sm text-gray-300 mb-1">Email</label>
          <input id="e" type="email" class="w-full bg-slate-900 text-white rounded-lg px-3 py-2 border border-slate-700 focus:outline-none focus:ring-2 focus:ring-purple-500" placeholder="jane@example.com" />
        </div>
        <button type="submit" class="w-full px-4 py-2 bg-purple-600 hover:bg-purple-700 rounded-lg font-medium">Continue</button>
      </form>

      <p class="text-xs text-gray-500 mt-4">Local-only auth for development. Do not enable in production.</p>
    </div>
  </main>

  <footer class="border-t border-slate-800 py-6 text-center text-sm text-gray-500">
    Powered by Sora · TestProvider
  </footer>

  <script>
    const u = document.getElementById('u');
    const e = document.getElementById('e');
    // Prefill from previous values
    u.value = localStorage.getItem('tp_u')||'';
    e.value = localStorage.getItem('tp_e')||'';
    document.getElementById('f').addEventListener('submit', ev => {
      ev.preventDefault();
      // Persist for convenience
      localStorage.setItem('tp_u', u.value);
      localStorage.setItem('tp_e', e.value);
      // Issue simple cookie consumed by TestProvider on reload
      document.cookie = `_tp_user=${encodeURIComponent(u.value)}|${encodeURIComponent(e.value)}; path=/`;
      // Reload to complete authorize flow (server will redirect when cookie is present)
      location.reload();
    });
  </script>
  <noscript>Enable JavaScript.</noscript>
</body>
</html>
""";
            return new ContentResult { ContentType = "text/html", Content = html };
        }

        // At this point userCookie is present and non-empty.
        var decoded = Uri.UnescapeDataString(userCookie ?? string.Empty);
        var parts = decoded.Split('|');
        var profile = new UserProfile(parts.ElementAtOrDefault(0) ?? "dev", parts.ElementAtOrDefault(1) ?? "dev@example.com", null);
        var code = store.IssueCode(profile, TimeSpan.FromMinutes(5), code_challenge);
        var uri = new UriBuilder(redirect_uri);
        var q = System.Web.HttpUtility.ParseQueryString(uri.Query);
        q["code"] = code; if (!string.IsNullOrWhiteSpace(state)) q["state"] = state;
        uri.Query = q.ToString()!;
  logger.LogDebug("TestProvider authorize: issuing code and redirecting to {Redirect}", uri.ToString());
  return Redirect(uri.ToString());
    }
}
