using Microsoft.AspNetCore.Mvc;
using Koan.Web.Controllers;
using S10.DevPortal.Models;

namespace S10.DevPortal.Controllers;

/// <summary>
/// Minimal controller - framework provides full CRUD automatically
/// Demonstrates bulk operations, set routing, capability detection
/// </summary>
[Route("api/[controller]")]
public class ArticlesController : EntityController<Article>
{
    // Inherits all CRUD operations automatically:
    // GET / - Collection with pagination, sorting, filtering
    // POST /query - Complex query with JSON filters
    // GET /{id} - Get by ID with relationship expansion
    // POST / - Upsert entity
    // POST /bulk - Bulk upsert operations
    // DELETE /{id} - Delete by ID
    // DELETE /bulk - Bulk delete by IDs
    // Plus set routing: ?set=published, ?set=drafts

    /// <summary>
    /// Custom endpoint demonstrating bulk operations capability
    /// </summary>
    [HttpPost("bulk-import")]
    public async Task<IActionResult> BulkImport([FromBody] List<Article> articles)
    {
        // Demonstrates framework bulk capabilities
        var upserted = await Article.UpsertMany(articles);
        return Ok(new { imported = upserted });
    }

    /// <summary>
    /// Custom endpoint demonstrating bulk delete capability
    /// </summary>
    [HttpDelete("bulk-delete")]
    public async Task<IActionResult> BulkDelete([FromBody] List<string> ids)
    {
        var count = await Article.Remove(ids);
        return Ok(new { deleted = count });
    }

    /// <summary>
    /// Set routing demonstration - published articles
    /// </summary>
    [HttpGet("published")]
    public async Task<IActionResult> GetPublished(CancellationToken ct)
    {
        // Framework automatically applies ?set=published
        return await GetCollection(ct);
    }

    /// <summary>
    /// Set routing demonstration - draft articles
    /// </summary>
    [HttpGet("drafts")]
    public async Task<IActionResult> GetDrafts(CancellationToken ct)
    {
        // Framework automatically applies ?set=drafts
        return await GetCollection(ct);
    }
}