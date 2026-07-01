using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc;
using S6.SnapVault.Models;
using S6.SnapVault.Services.AI;

namespace S6.SnapVault.Controllers;

/// <summary>
/// #20 — the active analysis-style library that populates the reroll split-button. A read-only projection of the
/// seeded (<c>[HostScoped]</c>, platform-shared) styles ordered by priority; the SPA reads <c>id</c>/<c>label</c>/
/// <c>icon</c> (falling back to <c>name</c>, which the projection maps to <c>label</c>). Deliberately minimal —
/// SnapVault seeds the styles at boot and there is no studio-facing style CRUD in this phase (custom per-studio
/// styles are a later scope; see <see cref="AnalysisStyle"/>'s remarks).
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
