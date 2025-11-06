using System.Threading;
using System.Threading.Tasks;
using Koan.Admin.Contracts;
using Koan.Admin.Services;
using Koan.Web.Admin.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Web.Admin.Controllers;

[ApiController]
[Authorize]
[ServiceFilter(typeof(KoanAdminAuthorizationFilter))]
[Route(KoanAdminRouteConvention.ApiPlaceholder + "/launchkit")]
[ApiExplorerSettings(GroupName = "KoanAdmin")]
public sealed class KoanAdminLaunchKitController : ControllerBase
{
    private readonly IKoanAdminFeatureManager _features;
    private readonly IKoanAdminLaunchKitService _launchKit;

    public KoanAdminLaunchKitController(
        IKoanAdminFeatureManager features,
        IKoanAdminLaunchKitService launchKit)
    {
        _features = features;
        _launchKit = launchKit;
    }

    [HttpGet("metadata")]
    public async Task<ActionResult<KoanAdminLaunchKitMetadata>> GetMetadata(CancellationToken cancellationToken)
    {
        if (!IsLaunchKitEnabled(out var result))
        {
            return result;
        }

        var metadata = await _launchKit.GetMetadataAsync(cancellationToken);
        return Ok(metadata);
    }

    [HttpPost("bundle")]
    public async Task<IActionResult> GenerateBundle(
        [FromBody] KoanAdminLaunchKitRequest? request,
        CancellationToken cancellationToken)
    {
        if (!IsLaunchKitEnabled(out var result))
        {
            return result;
        }

        var archive = await _launchKit.GenerateArchiveAsync(request ?? new KoanAdminLaunchKitRequest(null, null, null, null, null, null, null), cancellationToken)
            ;

        return File(archive.Content, archive.ContentType, archive.FileName);
    }

    private bool IsLaunchKitEnabled(out ActionResult result)
    {
        var snapshot = _features.Current;
        if (!snapshot.Enabled || !snapshot.WebEnabled)
        {
            result = NotFound();
            return false;
        }

        if (!snapshot.LaunchKitEnabled)
        {
            result = StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "Koan LaunchKit is disabled for this host. Enable Koan:Admin:EnableLaunchKit to allow bundle generation."
            });
            return false;
        }

        result = default!;
        return true;
    }
}
