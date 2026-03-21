using Microsoft.AspNetCore.Mvc;
using S18.Prism.Services;

namespace S18.Prism.Controllers;

[ApiController]
[Route("api/pulse")]
public class PulseController : ControllerBase
{
    private readonly IPulseService _pulseService;
    private readonly ILogger<PulseController> _logger;

    public PulseController(
        IPulseService pulseService,
        ILogger<PulseController> logger)
    {
        _pulseService = pulseService;
        _logger = logger;
    }

    /// <summary>
    /// GET /api/pulse/{spaceId}
    /// Returns a "what's new" briefing for the given space
    /// </summary>
    [HttpGet("{spaceId}")]
    public async Task<IActionResult> GetPulse(string spaceId, CancellationToken ct = default)
    {
        try
        {
            var briefing = await _pulseService.GenerateAsync(spaceId, ct);
            return Ok(briefing);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate pulse for space {SpaceId}", spaceId);
            return StatusCode(500, new { Error = "Failed to generate pulse briefing" });
        }
    }
}
