using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Web.Auth.TestProvider.Options;
using System.Reflection;

namespace Sora.Web.Auth.TestProvider.Controllers;

[ApiController]
public sealed class StaticController(IHostEnvironment env, IOptionsSnapshot<TestProviderOptions> opts) : ControllerBase
{
    [HttpGet]
    [Route(".testoauth/login.html")]
    public IActionResult LoginPage()
    {
        var o = opts.Value;
        if (!(env.IsDevelopment() || o.Enabled)) return NotFound();
        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "testprovider-login.html");
        if (System.IO.File.Exists(path))
        {
            var html = System.IO.File.ReadAllText(path);
            return new ContentResult { ContentType = "text/html", Content = html };
        }
        // Fallback: serve from embedded resource to survive publish/container scenarios
        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("testprovider-login.html", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrEmpty(resName))
        {
            using var s = asm.GetManifestResourceStream(resName);
            if (s != null)
            {
                using var reader = new StreamReader(s);
                var html = reader.ReadToEnd();
                return new ContentResult { ContentType = "text/html", Content = html };
            }
        }
        return NotFound();
    }
}