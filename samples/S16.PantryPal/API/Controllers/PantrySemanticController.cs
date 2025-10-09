using Microsoft.AspNetCore.Mvc;

namespace S16.PantryPal.Controllers;

/// <summary>
/// Placeholder for future semantic / vector-enhanced pantry search.
/// Will translate natural language intent into structured filters or vector queries.
/// </summary>
[ApiController]
[Route("api/pantry-semantic")] 
public class PantrySemanticController : ControllerBase
{
    [HttpPost("query")]
    public IActionResult Query([FromBody] object? body)
    {
        return StatusCode(501, new { error = "semantic search not yet implemented", planned = true });
    }
}