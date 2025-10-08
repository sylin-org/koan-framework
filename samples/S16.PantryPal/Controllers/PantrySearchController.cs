using Microsoft.AspNetCore.Mvc;
using S16.PantryPal.Services;

namespace S16.PantryPal.Controllers;

[ApiController]
[Route("api/pantry/search")] // distinct from data routes
public sealed class PantrySearchController : ControllerBase
{
    private readonly IPantrySearchService _svc;
    public PantrySearchController(IPantrySearchService svc) => _svc = svc;

    /// <summary>
    /// Search pantry items using semantic (vector) when available, otherwise lexical fallback.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? q, [FromQuery] int? topK, CancellationToken ct)
    {
        var (items, degraded) = await _svc.SearchAsync(q, topK, ct);
        Response.Headers["X-Search-Degraded"] = degraded ? "1" : "0";
        return Ok(items);
    }
}
