using Microsoft.AspNetCore.Mvc;
using S8.Location.Core.Models;
using Koan.Data.Core;
using Koan.Messaging;

namespace S8.Location.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LocationsController : ControllerBase
{
    private readonly ILogger<LocationsController> _logger;

    public LocationsController(ILogger<LocationsController> logger)
    {
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Core.Models.Location>>> GetLocations(
        [FromQuery] int page = 1, 
        [FromQuery] int size = 50)
    {
        var allLocations = await Core.Models.Location.All();
        var locations = allLocations
            .Skip((page - 1) * size)
            .Take(size)
            .ToList();

        return Ok(locations);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Core.Models.Location>> GetLocation(string id)
    {
        var location = await Core.Models.Location.Get(id);
        if (location == null)
            return NotFound();

        return Ok(location);
    }

    [HttpPost]
    public async Task<ActionResult<Core.Models.Location>> CreateLocation([FromBody] CreateLocationRequest request)
    {
        var location = new Core.Models.Location
        {
            Id = request.ExternalId ?? Guid.NewGuid().ToString(),
            Address = request.Address
        };

        // Send through Flow for orchestration
        await location.Send();

        return CreatedAtAction(nameof(GetLocation), new { id = location.Id }, location);
    }

    [HttpGet("{id}/canonical")]
    public async Task<ActionResult<AgnosticLocation?>> GetCanonicalLocation(string id)
    {
        var location = await Core.Models.Location.Get(id);
        if (location?.AgnosticLocationId == null)
            return NotFound();

        var canonical = await AgnosticLocation.Get(location.AgnosticLocationId);
        return Ok(canonical);
    }

    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<Core.Models.Location>>> SearchByAddress([FromQuery] string address)
    {
        var locations = await Core.Models.Location.Query($"Address LIKE '%{address}%'");
        return Ok(locations);
    }

    [HttpPost("test/{source}")]
    public async Task<ActionResult<Core.Models.Location>> TestSource(string source, [FromBody] TestLocationRequest request)
    {
        var location = new Core.Models.Location
        {
            Id = request.ExternalId,
            Address = request.Address
        };

        _logger.LogInformation("Test location from {Source}: {ExternalId} -> {Address}", 
            source, request.ExternalId, request.Address);

        // Send through Flow for orchestration
        await location.Send();

        return Ok(location);
    }

    [HttpPost("resolve")]
    public async Task<ActionResult<ResolveAddressResponse>> ResolveAddress([FromBody] ResolveAddressRequest request)
    {
        try
        {
            var resolver = HttpContext.RequestServices.GetRequiredService<Core.Services.AddressResolutionService>();
            var canonicalId = await resolver.ResolveToCanonicalIdAsync(request.Address);
            
            // Try to get the corrected address from cache
            var aiCorrected = request.Address; // Default to original
            // In a real implementation, you'd retrieve the AI-corrected version from the resolution cache
            
            return Ok(new ResolveAddressResponse(
                Original: request.Address,
                Corrected: aiCorrected,
                CanonicalId: canonicalId,
                Confidence: 0.95 // Placeholder confidence score
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve address: {Address}", request.Address);
            return BadRequest($"Failed to resolve address: {ex.Message}");
        }
    }

    [HttpGet("stats")]
    public async Task<ActionResult<ProcessingStatsResponse>> GetStats()
    {
        try
        {
            var allLocations = await Core.Models.Location.All();
            var total = allLocations.Count();
            // Flow pipeline stages track status, not entity properties
            var active = allLocations.Count(l => !string.IsNullOrEmpty(l.AgnosticLocationId));
            // AI corrections based on locations with canonical IDs
            var aiCorrected = allLocations.Count(l => !string.IsNullOrEmpty(l.AgnosticLocationId));
            
            return Ok(new ProcessingStatsResponse(
                TotalProcessed: total,
                SuccessRate: total > 0 ? Math.Round((double)active / total * 100, 1) : 0,
                AiCorrectionRate: total > 0 ? Math.Round((double)aiCorrected / total * 100, 1) : 0,
                AverageProcessingTime: 850, // Placeholder - would need to track actual processing times
                CacheHitRate: 65 // Placeholder - would need to track cache hits
            ));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get processing statistics");
            return BadRequest($"Failed to get statistics: {ex.Message}");
        }
    }
}

public record CreateLocationRequest(string Address, string? ExternalId = null);
public record TestLocationRequest(string ExternalId, string Address);
public record ResolveAddressRequest(string Address);
public record ResolveAddressResponse(string Original, string Corrected, string CanonicalId, double Confidence);
public record ProcessingStatsResponse(int TotalProcessed, double SuccessRate, double AiCorrectionRate, double AverageProcessingTime, double CacheHitRate);