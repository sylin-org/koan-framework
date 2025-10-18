using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Web.Controllers;
using Koan.Web.Attributes;
using S6.SnapVault.Models;
using S6.SnapVault.Services.AI;

namespace S6.SnapVault.Controllers;

/// <summary>
/// Analysis styles API - manages AI analysis style configurations
/// Inherits CRUD from EntityController (Koan Framework pattern)
/// </summary>
[Route("api/analysis-styles")]
[Pagination(Mode = PaginationMode.On, DefaultSize = 20, MaxSize = 100, DefaultSort = "priority")]
public class AnalysisStylesController : EntityController<AnalysisStyle>
{
    private readonly ILogger<AnalysisStylesController> _logger;

    public AnalysisStylesController(ILogger<AnalysisStylesController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Get all active analysis styles ordered by priority
    /// Used by frontend dropdown to populate style selection
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<IEnumerable<AnalysisStyleDefinition>>> GetActiveStyles(CancellationToken ct = default)
    {
        var styles = await AnalysisStyle.Query(s => s.IsActive, ct);

        var definitions = styles
            .OrderBy(s => s.Priority)
            .Select(s => new AnalysisStyleDefinition(
                Id: s.Id,
                Label: s.Name,
                Icon: s.Icon,
                Description: s.Description,
                Priority: s.Priority
            ))
            .ToList();

        return Ok(definitions);
    }

    /// <summary>
    /// Get system (default) styles only
    /// </summary>
    [HttpGet("system")]
    public async Task<ActionResult<IEnumerable<AnalysisStyle>>> GetSystemStyles(CancellationToken ct = default)
    {
        var styles = await AnalysisStyle.Query(s => s.IsSystemStyle && s.IsActive, ct);
        return Ok(styles.OrderBy(s => s.Priority));
    }

    /// <summary>
    /// Get user-created styles only
    /// </summary>
    [HttpGet("user")]
    public async Task<ActionResult<IEnumerable<AnalysisStyle>>> GetUserStyles(CancellationToken ct = default)
    {
        var styles = await AnalysisStyle.Query(s => s.IsUserCreated && s.IsActive, ct);
        return Ok(styles.OrderBy(s => s.Priority));
    }

    /// <summary>
    /// Create a new custom analysis style
    /// Users can create styles via parameter customization (safe)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<AnalysisStyle>> Post([FromBody] AnalysisStyle style, CancellationToken ct = default)
    {
        // Mark as user-created
        style.IsUserCreated = true;
        style.IsSystemStyle = false;
        style.IsActive = true;
        style.CreatedAt = DateTime.UtcNow;

        // Validate: don't allow full prompt override from API (security)
        if (!string.IsNullOrEmpty(style.FullPromptOverride))
        {
            _logger.LogWarning("Rejected user style creation with FullPromptOverride (security)");
            return BadRequest(new { Error = "FullPromptOverride not allowed via API. Use parameter-based customization." });
        }

        await style.Save(ct);

        _logger.LogInformation("User created custom analysis style: {StyleName} ({StyleId})", style.Name, style.Id);

        return CreatedAtRoute("GetById", new { id = style.Id }, style);
    }

    /// <summary>
    /// Update an existing analysis style
    /// System styles can be disabled but not modified
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<AnalysisStyle>> Put(string id, [FromBody] AnalysisStyle updatedStyle, CancellationToken ct = default)
    {
        var existing = await AnalysisStyle.Get(id, ct);
        if (existing == null)
        {
            return NotFound();
        }

        // System styles can only be disabled, not modified
        if (existing.IsSystemStyle && !updatedStyle.IsActive && updatedStyle.IsActive != existing.IsActive)
        {
            existing.IsActive = false;
            existing.UpdatedAt = DateTime.UtcNow;
            await existing.Save(ct);

            _logger.LogInformation("Disabled system style: {StyleName}", existing.Name);
            return Ok(existing);
        }

        if (existing.IsSystemStyle)
        {
            return BadRequest(new { Error = "System styles cannot be modified. Create a custom style instead." });
        }

        // User styles can be fully modified
        updatedStyle.Id = id; // Preserve ID
        updatedStyle.UpdatedAt = DateTime.UtcNow;
        updatedStyle.IsUserCreated = true; // Ensure it stays marked as user-created
        updatedStyle.IsSystemStyle = false;

        // Security: don't allow full prompt override from API
        if (!string.IsNullOrEmpty(updatedStyle.FullPromptOverride))
        {
            return BadRequest(new { Error = "FullPromptOverride not allowed via API" });
        }

        await updatedStyle.Save(ct);

        _logger.LogInformation("Updated user style: {StyleName} ({StyleId})", updatedStyle.Name, id);

        return Ok(updatedStyle);
    }

    /// <summary>
    /// Delete a user-created style (soft delete)
    /// System styles cannot be deleted, only disabled
    /// </summary>
    [HttpDelete("{id}")]
    public override async Task<IActionResult> Delete(string id, CancellationToken ct = default)
    {
        var style = await AnalysisStyle.Get(id, ct);
        if (style == null)
        {
            return NotFound();
        }

        if (style.IsSystemStyle)
        {
            return BadRequest(new { Error = "System styles cannot be deleted. Use PUT to disable instead." });
        }

        // Soft delete
        style.IsActive = false;
        style.UpdatedAt = DateTime.UtcNow;
        await style.Save(ct);

        _logger.LogInformation("Deleted (soft) user style: {StyleName} ({StyleId})", style.Name, id);

        return NoContent();
    }
}
