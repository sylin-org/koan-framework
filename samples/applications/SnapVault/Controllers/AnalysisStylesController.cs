using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc;
using SnapVault.Models;
using SnapVault.Services.AI;

namespace SnapVault.Controllers;

/// <summary>
/// Read-only projection of the shared, seeded analysis-style library ordered for the application UI.
/// </summary>
[ApiController]
[Route("api/analysis-styles")]
public sealed class AnalysisStylesController : ControllerBase
{
    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<AnalysisStyleDefinition>>> GetActive(CancellationToken ct = default)
    {
        var styles = await AnalysisStyle.Query(s => s.IsActive, ct);
        var definitions = styles
            .OrderBy(s => s.Priority)
            .Select(s => new AnalysisStyleDefinition(
                Id: s.Id,
                Label: s.Name,
                Icon: s.Icon,
                Description: s.Description,
                Priority: s.Priority))
            .ToList();
        return Ok(definitions);
    }
}
