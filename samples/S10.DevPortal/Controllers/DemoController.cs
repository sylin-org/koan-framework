using Microsoft.AspNetCore.Mvc;
using Koan.Data.Core;
using Koan.Data.Abstractions;
using S10.DevPortal.Models;
using S10.DevPortal.Services;

namespace S10.DevPortal.Controllers;

/// <summary>
/// Demo controller showcasing unique Koan Framework capabilities
/// Provides provider switching, capability detection, and performance comparison
/// </summary>
[Route("api/[controller]")]
[ApiController]
public class DemoController : ControllerBase
{
    private readonly IDemoSeedService _seedService;

    public DemoController(IDemoSeedService seedService)
    {
        _seedService = seedService;
    }

    /// <summary>
    /// Switch data provider for demonstration purposes
    /// </summary>
    [HttpPost("switch-provider/{provider}")]
    public async Task<IActionResult> SwitchProvider(string provider)
    {
        try
        {
            // Use EntityContext for scoped provider switching
            using var context = EntityContext.With(provider);

            // Verify provider is working by counting existing records
            var articleCount = (await Article.All()).Count;

            return Ok(new
            {
                provider = provider,
                status = "switched",
                articleCount = articleCount,
                message = $"Successfully switched to {provider} provider"
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                provider = provider,
                status = "failed",
                error = ex.Message
            });
        }
    }

    /// <summary>
    /// Get current provider capabilities
    /// </summary>
    [HttpGet("capabilities")]
    public IActionResult GetCapabilities()
    {
        var articleQueryCaps = Data<Article, string>.QueryCaps;
        var articleWriteCaps = Data<Article, string>.WriteCaps;

        return Ok(new
        {
            provider = "current", // TODO: Get actual provider name
            query = new
            {
                capabilities = articleQueryCaps.Capabilities.ToString(),
                supportsLinq = articleQueryCaps.Capabilities.HasFlag(QueryCapabilities.Linq),
                supportsString = articleQueryCaps.Capabilities.HasFlag(QueryCapabilities.String)
            },
            write = new
            {
                capabilities = articleWriteCaps.Writes.ToString(),
                supportsBulkUpsert = articleWriteCaps.Writes.HasFlag(WriteCapabilities.BulkUpsert),
                supportsBulkDelete = articleWriteCaps.Writes.HasFlag(WriteCapabilities.BulkDelete)
            },
            entities = new
            {
                article = "Entity<Article> with auto GUID v7",
                technology = "Entity<Technology> with hierarchical relationships",
                comment = "Entity<Comment> with threaded structure",
                user = "Entity<User> basic pattern"
            }
        });
    }

    /// <summary>
    /// Performance comparison across providers
    /// </summary>
    [HttpGet("performance-comparison")]
    public async Task<IActionResult> PerformanceComparison()
    {
        var results = new List<object>();
        var providers = new[] { "sqlite", "mongodb", "postgresql" };

        foreach (var provider in providers)
        {
            try
            {
                var start = DateTime.UtcNow;
                using var context = EntityContext.With(provider);

                // Simple performance test - count all articles
                var count = (await Article.All()).Count;
                var duration = DateTime.UtcNow - start;

                results.Add(new
                {
                    provider = provider,
                    recordCount = count,
                    queryDurationMs = duration.TotalMilliseconds,
                    status = "success"
                });
            }
            catch (Exception ex)
            {
                results.Add(new
                {
                    provider = provider,
                    recordCount = 0,
                    queryDurationMs = 0,
                    status = "failed",
                    error = ex.Message
                });
            }
        }

        return Ok(new
        {
            comparison = results,
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Bulk operations demonstration
    /// </summary>
    [HttpPost("bulk-demo")]
    public async Task<IActionResult> BulkDemo([FromQuery] int count = 100)
    {
        count = Math.Clamp(count, 1, 1000); // Limit for demo safety

        var start = DateTime.UtcNow;

        // Generate sample articles
        var articles = await _seedService.GenerateSampleArticles(count);

        // Bulk insert
        var insertStart = DateTime.UtcNow;
        var upserted = await Article.UpsertMany(articles);
        var insertDuration = DateTime.UtcNow - insertStart;

        // Bulk query
        var queryStart = DateTime.UtcNow;
        var retrieved = await Article.All();
        var queryDuration = DateTime.UtcNow - queryStart;

        var totalDuration = DateTime.UtcNow - start;

        return Ok(new
        {
            operation = "bulk-demo",
            recordsGenerated = count,
            recordsUpserted = upserted,
            recordsRetrieved = retrieved.Count,
            timing = new
            {
                totalMs = totalDuration.TotalMilliseconds,
                insertMs = insertDuration.TotalMilliseconds,
                queryMs = queryDuration.TotalMilliseconds
            },
            capabilities = Data<Article, string>.WriteCaps.Writes.ToString()
        });
    }

    /// <summary>
    /// Seed demo data across all entities
    /// </summary>
    [HttpPost("seed-demo-data")]
    public async Task<IActionResult> SeedDemoData()
    {
        try
        {
            var result = await _seedService.SeedDemoData();
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Clear all demo data
    /// </summary>
    [HttpDelete("clear-demo-data")]
    public async Task<IActionResult> ClearDemoData()
    {
        try
        {
            var commentsDeleted = await Comment.RemoveAll();
            var articlesDeleted = await Article.RemoveAll();
            var technologiesDeleted = await Technology.RemoveAll();
            var usersDeleted = await Models.User.RemoveAll();

            return Ok(new
            {
                deleted = new
                {
                    comments = commentsDeleted,
                    articles = articlesDeleted,
                    technologies = technologiesDeleted,
                    users = usersDeleted
                }
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Relationship navigation demonstration
    /// </summary>
    [HttpGet("relationship-demo")]
    public async Task<IActionResult> RelationshipDemo()
    {
        // Get a sample article with relationships
        var articles = await Article.All();
        var article = articles.FirstOrDefault();

        if (article == null)
        {
            return BadRequest(new { error = "No articles found. Please seed demo data first." });
        }

        // Manually fetch related entities
        var author = !string.IsNullOrEmpty(article.AuthorId)
            ? await Models.User.Get(article.AuthorId)
            : null;

        var technology = !string.IsNullOrEmpty(article.TechnologyId)
            ? await Technology.Get(article.TechnologyId)
            : null;

        var comments = await Comment.All();
        var articleComments = comments.Where(c => c.ArticleId == article.Id).ToList();

        return Ok(new
        {
            article = article,
            author = author,
            technology = technology,
            comments = articleComments,
            totalEntities = new
            {
                articles = articles.Count,
                comments = comments.Count,
                users = (await Models.User.All()).Count,
                technologies = (await Technology.All()).Count
            }
        });
    }
}