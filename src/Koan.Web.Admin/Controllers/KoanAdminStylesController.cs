using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Koan.Admin.Services;
using Koan.Web.Admin.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.Admin.Controllers;

[ApiController]
[Authorize]
[ServiceFilter(typeof(KoanAdminAuthorizationFilter))]
[Route(KoanAdminRouteConvention.ApiPlaceholder)]
[ApiExplorerSettings(GroupName = "KoanAdmin")]
public sealed class KoanAdminStylesController : ControllerBase
{
    private readonly IKoanAdminFeatureManager _features;
    private readonly IKoanAdminManifestService _manifest;

    public KoanAdminStylesController(IKoanAdminFeatureManager features, IKoanAdminManifestService manifest)
    {
        _features = features;
        _manifest = manifest;
    }

    [HttpGet("status/styles/module-visuals.css")]
    [Produces("text/css")]
    public async Task<IActionResult> GetModuleStyles(CancellationToken cancellationToken)
    {
        var snapshot = _features.Current;
        if (!snapshot.Enabled || !snapshot.WebEnabled)
        {
            return NotFound();
        }

        var manifest = await _manifest.BuildAsync(cancellationToken).ConfigureAwait(false);
        var styles = KoanAdminModuleStyleResolver.ResolveAll(manifest.Modules);
        var css = KoanAdminModuleStyleResolver.BuildStylesheet(styles);

        var cssBytes = Encoding.UTF8.GetBytes(css);
        Response.Headers["Cache-Control"] = "no-store, max-age=0";
        Response.Headers["Pragma"] = "no-cache";
        Response.Headers["Expires"] = "0";

        return File(cssBytes, "text/css; charset=utf-8");
    }
}
