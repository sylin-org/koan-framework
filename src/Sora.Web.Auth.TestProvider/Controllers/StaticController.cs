using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Sora.Web.Auth.TestProvider.Options;

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
        if (!System.IO.File.Exists(path)) return NotFound();
        var html = System.IO.File.ReadAllText(path);
        return new ContentResult { ContentType = "text/html", Content = html };
    }
}