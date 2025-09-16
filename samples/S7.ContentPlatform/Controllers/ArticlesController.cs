using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Web.Controllers;
using S7.ContentPlatform.Models;

namespace S7.ContentPlatform.Controllers;

/// <summary>
/// CRUD operations for articles. 
/// Moderation and soft-delete capabilities are handled by the generic controllers
/// registered in Program.cs for the "api/articles" route.
/// </summary>
[Route("api/articles")]
public sealed class ArticlesController : EntityController<Article>
{
    /// <summary>
    /// Get published articles (excludes drafts and under-review articles).
    /// </summary>
    [HttpGet("published")]
    public async Task<ActionResult<IReadOnlyList<Article>>> GetPublished(CancellationToken ct)
    {
        var articles = await Data<Article, string>.All(ct);
        var published = articles
            .Where(a => a.Status == ArticleStatus.Published)
            .OrderByDescending(a => a.PublishedAt)
            .ToList();
        return Ok(published);
    }
    
    /// <summary>
    /// Get articles by category.
    /// </summary>
    [HttpGet("by-category/{categoryId}")]
    public async Task<ActionResult<IReadOnlyList<Article>>> GetByCategory(string categoryId, CancellationToken ct)
    {
        var articles = await Data<Article, string>.All(ct);
        var categoryArticles = articles
            .Where(a => a.CategoryId == categoryId && a.Status == ArticleStatus.Published)
            .OrderByDescending(a => a.PublishedAt)
            .ToList();
        return Ok(categoryArticles);
    }
    
    /// <summary>
    /// Get articles by author.
    /// </summary>
    [HttpGet("by-author/{authorId}")]
    public async Task<ActionResult<IReadOnlyList<Article>>> GetByAuthor(string authorId, CancellationToken ct)
    {
        var articles = await Data<Article, string>.All(ct);
        var authorArticles = articles
            .Where(a => a.AuthorId == authorId && a.Status == ArticleStatus.Published)
            .OrderByDescending(a => a.PublishedAt)
            .ToList();
        return Ok(authorArticles);
    }
}
