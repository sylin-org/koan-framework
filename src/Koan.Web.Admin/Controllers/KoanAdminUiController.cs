using Koan.Web.Admin.Infrastructure;
using Koan.Web.Admin.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Koan.Web.Admin.Controllers;

[ServiceFilter(typeof(KoanAdminAuthorizationFilter))]
[Route(KoanAdminRouteConvention.RootPlaceholder)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class KoanAdminUiController(
    IOptions<KoanAdminOptions> options,
    IHostEnvironment environment) : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        if (!IsActive())
        {
            return NotFound();
        }

        if (!Request.Path.Value?.EndsWith('/') ?? false)
        {
            return Redirect(Request.Path.Value + "/");
        }

        return Serve("index.html");
    }

    [HttpGet("{**asset}")]
    public IActionResult Assets(string? asset)
        => IsActive() ? Serve(asset) : NotFound();

    private bool IsActive() => environment.IsDevelopment() && options.Value.Enabled;

    private IActionResult Serve(string? asset)
    {
        if (!KoanAdminUiAssetProvider.TryGetAsset(asset, out var content, out var contentType))
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "no-store";
        return Content(content, contentType);
    }
}
