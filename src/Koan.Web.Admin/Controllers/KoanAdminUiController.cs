using Koan.Admin.Services;
using Koan.Web.Admin.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.Admin.Controllers;

[Authorize]
[ServiceFilter(typeof(KoanAdminAuthorizationFilter))]
[Route(KoanAdminRouteConvention.RootPlaceholder)]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class KoanAdminUiController : Controller
{
    private readonly IKoanAdminFeatureManager _features;

    public KoanAdminUiController(IKoanAdminFeatureManager features)
    {
        _features = features;
    }

    [HttpGet("")]
    public IActionResult Index()
        => Serve("index.html");

    [HttpGet("{**asset}")]
    public IActionResult Assets(string? asset)
        => Serve(asset);

    private IActionResult Serve(string? asset)
    {
        var snapshot = _features.Current;
        if (!snapshot.Enabled || !snapshot.WebEnabled)
        {
            return NotFound();
        }

        if (!KoanAdminUiAssetProvider.TryGetAsset(asset, out var content, out var contentType))
        {
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "no-store";
        return Content(content, contentType);
    }
}
