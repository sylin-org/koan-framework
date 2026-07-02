using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Koan.Tenancy.Web.Authorization;
using Koan.Tenancy.Web.Infrastructure;

namespace Koan.Tenancy.Web.Controllers;

/// <summary>
/// Serves the bundled operator-console UI (ARCH-0104) at <c>/tenancy</c>. Gated on the same
/// <see cref="TenancyWebPolicies.Operator"/> policy as the API (dev-open just-works; prod-closed fails closed), so
/// the human surface and the data surface share one gate.
/// </summary>
[Authorize(Policy = TenancyWebPolicies.Operator)]
[Route("tenancy")]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class TenancyConsoleUiController : Controller
{
    [HttpGet("")]
    public IActionResult Index()
    {
        // Redirect to the trailing slash so the UI's relative asset paths resolve under /tenancy/.
        if (!(Request.Path.Value?.EndsWith('/') ?? false))
            return Redirect((Request.PathBase + Request.Path).ToString().TrimEnd('/') + "/");
        return Serve("index.html");
    }

    [HttpGet("{**asset}")]
    public IActionResult Assets(string? asset) => Serve(asset);

    private IActionResult Serve(string? asset)
    {
        if (!TenancyConsoleAssetProvider.TryGetAsset(asset, out var content, out var contentType))
            return NotFound();
        Response.Headers["Cache-Control"] = "no-store";
        // A privileged cross-tenant console: lock the origin down. The UI is fully self-contained (no inline script or
        // style, no third-party assets), so a strict same-origin CSP holds and blunts any same-origin XSS that could
        // otherwise reach the /api/tenancy/admin endpoints.
        if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            Response.Headers["Content-Security-Policy"] =
                "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; frame-ancestors 'none'; base-uri 'none'; form-action 'none'";
        return File(content, contentType);
    }
}
