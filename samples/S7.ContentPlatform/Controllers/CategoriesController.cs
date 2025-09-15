using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Web.Controllers;
using S7.ContentPlatform.Models;

namespace S7.ContentPlatform.Controllers;

/// <summary>
/// CRUD operations for categories.
/// </summary>
[Route("api/categories")]
public sealed class CategoriesController : EntityController<Category>
{
    /// <summary>
    /// Get active categories ordered by sort order.
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<IReadOnlyList<Category>>> GetActive(CancellationToken ct)
    {
        var categories = await Data<Category, string>.All(ct);
        var active = categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToList();
        return Ok(active);
    }
    
    /// <summary>
    /// Get category with article count.
    /// </summary>
    [HttpGet("{id}/with-stats")]
    public async Task<ActionResult<object>> GetWithStats(string id, CancellationToken ct)
    {
        var category = await Data<Category, string>.GetAsync(id, ct);
        if (category is null) return NotFound();
        
        var articles = await Data<Article, string>.All(ct);
        var categoryArticles = articles.Where(a => a.CategoryId == id).ToList();
        
        var result = new
        {
            Category = category,
            Stats = new
            {
                TotalArticles = categoryArticles.Count,
                PublishedArticles = categoryArticles.Count(a => a.Status == ArticleStatus.Published),
                DraftArticles = categoryArticles.Count(a => a.Status == ArticleStatus.Draft),
                LastPublished = categoryArticles
                    .Where(a => a.Status == ArticleStatus.Published)
                    .Max(a => a.PublishedAt)
            }
        };
        
        return Ok(result);
    }
}
