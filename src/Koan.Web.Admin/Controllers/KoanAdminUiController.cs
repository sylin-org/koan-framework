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
    {
        // Redirect to trailing slash if not present (ensures relative paths resolve correctly)
        if (!Request.Path.Value?.EndsWith('/') ?? false)
        {
            return Redirect(Request.Path.Value + "/");
        }
        return Serve("index.html");
    }

    [HttpGet("{**asset}")]
    public IActionResult Assets(string? asset)
    {
        Console.WriteLine($"[DEBUG] KoanAdminUiController.Assets called with asset='{asset}'");
        return Serve(asset);
    }

    private IActionResult Serve(string? asset)
    {
        Console.WriteLine($"[DEBUG] Serve called with asset='{asset}'");
        var snapshot = _features.Current;
        Console.WriteLine($"[DEBUG] Admin enabled={snapshot.Enabled}, web={snapshot.WebEnabled}");
        if (!snapshot.Enabled || !snapshot.WebEnabled)
        {
            Console.WriteLine($"[DEBUG] Admin disabled, returning NotFound");
            return NotFound();
        }

        var found = KoanAdminUiAssetProvider.TryGetAsset(asset, out var content, out var contentType);
        Console.WriteLine($"[DEBUG] TryGetAsset('{asset}') returned {found}, contentType='{contentType}'");
        if (!found)
        {
            Console.WriteLine($"[DEBUG] Asset not found, returning NotFound");
            return NotFound();
        }

        Response.Headers["Cache-Control"] = "no-store";
        return Content(content, contentType);
    }
}
