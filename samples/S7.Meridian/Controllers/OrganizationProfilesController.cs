using System.Threading;
using System.Threading.Tasks;
using Koan.Samples.Meridian.Infrastructure;
using Koan.Samples.Meridian.Models;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;

namespace Koan.Samples.Meridian.Controllers;

[ApiController]
[Route(MeridianConstants.OrganizationProfiles.Route)]
public sealed class OrganizationProfilesController : EntityController<OrganizationProfile>
{
    /// <summary>
    /// Activates the specified OrganizationProfile and deactivates all others.
    /// Only one profile can be active at a time.
    /// </summary>
    [HttpPost("{id}/activate")]
    public async Task<IActionResult> Activate(string id, CancellationToken ct)
    {
        var profile = await OrganizationProfile.Get(id, ct);
        if (profile == null)
        {
            return NotFound(new { message = $"OrganizationProfile with ID '{id}' not found." });
        }

        await profile.ActivateAsync(ct);

        return Ok(new { message = $"OrganizationProfile '{profile.Name}' activated successfully.", active = true });
    }

    /// <summary>
    /// Gets the currently active OrganizationProfile, if any.
    /// </summary>
    [HttpGet("active")]
    public async Task<IActionResult> GetActive(CancellationToken ct)
    {
        var profile = await OrganizationProfile.GetActiveAsync(ct);
        if (profile == null)
        {
            return NotFound(new { message = "No active OrganizationProfile found." });
        }

        return Ok(profile);
    }
}
