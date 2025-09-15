using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using S7.TechDocs.Models;
using Koan.Web.Extensions.Authorization;

namespace S7.TechDocs.Controllers;

[Route("api/documents")]
public class DocumentModerationStatsController : ControllerBase
{
    /// <summary>
    /// Moderation pipeline stats for dashboard (submitted, approved today, denied total)
    /// GET: /api/documents/moderation/stats
    /// </summary>
    [Authorize]
    [RequireCapability(Koan.Web.Extensions.Capabilities.CapabilityActions.Moderation.Queue)]
    [HttpGet("moderation/stats")]
    public async Task<IActionResult> Stats(CancellationToken ct)
    {
        // Submitted count: items in Submitted set
        int submittedCount = 0;
    try { submittedCount = await Document.CountAll(Koan.Web.Infrastructure.KoanWebConstants.Sets.Moderation.Submitted, ct); } catch { submittedCount = 0; }

        // Approved today: Published documents with PublishedAt >= UTC midnight
        var startOfDayUtc = DateTime.UtcNow.Date;
        int approvedToday = 0;
        try
        {
            var all = await Document.All(ct);
            approvedToday = all.Count(d => d.PublishedAt.HasValue && d.PublishedAt.Value >= startOfDayUtc);
        }
        catch { }

        // Denied total: items in Denied set (no date tracking here)
        int deniedTotal = 0;
    try { deniedTotal = await Document.CountAll(Koan.Web.Infrastructure.KoanWebConstants.Sets.Moderation.Denied, ct); } catch { deniedTotal = 0; }

        return Ok(new { submitted = submittedCount, approvedToday, denied = deniedTotal });
    }
}
