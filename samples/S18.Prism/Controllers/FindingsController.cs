using Koan.Data.Core;
using Koan.Web.Attributes;
using Koan.Web.Controllers;
using Microsoft.AspNetCore.Mvc;
using S18.Prism.Models;

namespace S18.Prism.Controllers;

[Route("api/[controller]")]
[Pagination(Mode = PaginationMode.On, DefaultSize = 30, MaxSize = 200, DefaultSort = "-id")]
public class FindingsController : EntityController<ResearchFinding>
{
    private readonly ILogger<FindingsController> _logger;

    public FindingsController(ILogger<FindingsController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// POST /api/findings/{id}/approve
    /// Approve a research finding for ingestion
    /// </summary>
    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id, CancellationToken ct = default)
    {
        try
        {
            var finding = await ResearchFinding.Get(id, ct);
            if (finding is null)
                return NotFound(new { Error = $"Finding '{id}' not found" });

            finding.Status = FindingStatus.Approved;
            finding.ReviewStatus = Koan.AI.Review.ReviewStatus.Approved;
            finding.ReviewedAt = DateTime.UtcNow;
            await finding.Save(ct);

            _logger.LogInformation("Approved finding {FindingId}", id);

            return Ok(finding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to approve finding {FindingId}", id);
            return StatusCode(500, new { Error = "Failed to approve finding" });
        }
    }

    /// <summary>
    /// POST /api/findings/{id}/dismiss
    /// Dismiss a research finding
    /// </summary>
    [HttpPost("{id}/dismiss")]
    public async Task<IActionResult> Dismiss(
        string id,
        [FromBody] DismissRequest? request,
        CancellationToken ct = default)
    {
        try
        {
            var finding = await ResearchFinding.Get(id, ct);
            if (finding is null)
                return NotFound(new { Error = $"Finding '{id}' not found" });

            finding.Status = FindingStatus.Dismissed;
            finding.ReviewStatus = Koan.AI.Review.ReviewStatus.Rejected;
            finding.RejectionReason = request?.Reason;
            finding.ReviewedAt = DateTime.UtcNow;
            await finding.Save(ct);

            _logger.LogInformation("Dismissed finding {FindingId}: {Reason}", id, request?.Reason);

            return Ok(finding);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to dismiss finding {FindingId}", id);
            return StatusCode(500, new { Error = "Failed to dismiss finding" });
        }
    }
}

public class DismissRequest
{
    public string? Reason { get; set; }
}
