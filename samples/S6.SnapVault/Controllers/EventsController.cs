using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Web.Controllers;
using S6.SnapVault.Models;

namespace S6.SnapVault.Controllers;

/// <summary>
/// Event management API - demonstrates EntityController<T> pattern
/// Inherits full CRUD: GET /, POST /query, GET /{id}, POST /, DELETE /{id}
/// </summary>
[Route("api/[controller]")]
public class EventsController : EntityController<Event>
{
    private readonly ILogger<EventsController> _logger;

    public EventsController(ILogger<EventsController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Create new event - Custom validation before save
    /// </summary>
    [HttpPost]
    public override async Task<IActionResult> Upsert([FromBody] Event entity, CancellationToken ct = default)
    {
        // Validate event date
        if (entity.EventDate > DateTime.UtcNow.AddYears(1))
        {
            return BadRequest(new { Error = "Event date cannot be more than 1 year in the future" });
        }

        // Set initial values
        entity.CreatedAt = DateTime.UtcNow;
        entity.LastAccessedAt = DateTime.UtcNow;
        entity.CurrentTier = StorageTier.Hot;
        entity.PhotoCount = 0;

        await entity.Save(ct);

        _logger.LogInformation("Created event {EventId}: {EventName}", entity.Id, entity.Name);

        return Ok(entity);
    }

    /// <summary>
    /// Get event timeline - grouped by month/year
    /// </summary>
    [HttpGet("timeline")]
    public async Task<ActionResult<TimelineResponse>> GetTimeline(
        [FromQuery] int months = 12,
        [FromQuery] EventType? type = null,
        CancellationToken ct = default)
    {
        var cutoffDate = DateTime.UtcNow.AddMonths(-months);
        var allEvents = await Event.Query(e => e.EventDate >= cutoffDate, ct);

        var events = allEvents;
        if (type.HasValue)
        {
            events = events.Where(e => e.Type == type.Value).ToList();
        }

        events = events.OrderByDescending(e => e.EventDate).ToList();

        // Group by month/year
        var groups = events
            .GroupBy(e => new { e.EventDate.Year, e.EventDate.Month })
            .Select(g => new TimelineGroup
            {
                Year = g.Key.Year,
                Month = g.Key.Month,
                MonthName = new DateTime(g.Key.Year, g.Key.Month, 1, 0, 0, 0, DateTimeKind.Utc).ToString("MMMM"),
                Events = g.ToList()
            })
            .ToList();

        return Ok(new TimelineResponse
        {
            Groups = groups,
            TotalEvents = events.Count,
            TotalPhotos = events.Sum(e => e.PhotoCount)
        });
    }

    /// <summary>
    /// Get events by storage tier
    /// </summary>
    [HttpGet("by-tier/{tier}")]
    public async Task<ActionResult<List<Event>>> GetByTier(StorageTier tier)
    {
        var events = await Event.Query(e => e.CurrentTier == tier);
        return Ok(events);
    }

    /// <summary>
    /// Archive event to cold storage
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<ActionResult> ArchiveEvent(string id)
    {
        var evt = await Event.Get(id);
        if (evt == null)
        {
            return NotFound();
        }

        // TODO: Implement tier migration service
        evt.CurrentTier = StorageTier.Cold;
        await evt.Save();

        _logger.LogInformation("Archived event {EventId} to cold storage", id);

        return Ok(new { Message = "Event archived successfully", Event = evt });
    }
}

public class TimelineResponse
{
    public List<TimelineGroup> Groups { get; set; } = new();
    public int TotalEvents { get; set; }
    public int TotalPhotos { get; set; }
}

public class TimelineGroup
{
    public int Year { get; set; }
    public int Month { get; set; }
    public string MonthName { get; set; } = "";
    public List<Event> Events { get; set; } = new();
}
