using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Web.Controllers;
using S7.ContentPlatform.Models;

namespace S7.ContentPlatform.Controllers;

/// <summary>
/// CRUD operations for authors.
/// Soft-delete capabilities are handled by the generic controller
/// registered in Program.cs for the "api/authors" route.
/// </summary>
[Route("api/authors")]
public sealed class AuthorsController : EntityController<Author>
{
    /// <summary>
    /// Get active authors only.
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<IReadOnlyList<Author>>> GetActive(CancellationToken ct)
    {
        var authors = await Data<Author, string>.All(ct);
        var active = authors
            .Where(a => a.IsActive)
            .OrderBy(a => a.Name)
            .ToList();
        return Ok(active);
    }
    
    /// <summary>
    /// Get author statistics.
    /// </summary>
    [HttpGet("{id}/stats")]
    public async Task<ActionResult<object>> GetStats(string id, CancellationToken ct)
    {
        var author = await Data<Author, string>.GetAsync(id, ct);
        if (author is null) return NotFound();
        
        var articles = await Data<Article, string>.All(ct);
        var authorArticles = articles.Where(a => a.AuthorId == id).ToList();
        
        var stats = new
        {
            TotalArticles = authorArticles.Count,
            PublishedArticles = authorArticles.Count(a => a.Status == ArticleStatus.Published),
            DraftArticles = authorArticles.Count(a => a.Status == ArticleStatus.Draft),
            UnderReviewArticles = authorArticles.Count(a => a.Status == ArticleStatus.UnderReview),
            LastPublished = authorArticles
                .Where(a => a.Status == ArticleStatus.Published)
                .Max(a => a.PublishedAt),
            JoinedAt = author.JoinedAt
        };
        
        return Ok(stats);
    }
}
