using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.AspNetCore.Mvc;
using DevPortal.Models;

namespace DevPortal.Controllers;

[ApiController]
[Route("api/publication")]
public sealed class PublicationController(ILogger<PublicationController> logger) : ControllerBase
{
    [HttpPost("reset")]
    public async Task<IActionResult> Reset(CancellationToken ct)
    {
        using (EntityContext.Source(PublicationChannel.Preview.ToString()))
        {
            await Article.RemoveAll(RemoveStrategy.Safe, ct);
        }

        await Article.RemoveAll(RemoveStrategy.Safe, ct);

        Article[] articles =
        [
            new()
            {
                Id = "article-composition",
                Title = "Composition that explains itself",
                Summary = "How references, startup decisions, and facts describe one runtime.",
                UpdatedAt = DateTimeOffset.Parse("2026-07-15T09:00:00Z")
            },
            new()
            {
                Id = "article-entities",
                Title = "Business-first entities",
                Summary = "Keep persistence, APIs, and capability growth centered on readable domain code.",
                UpdatedAt = DateTimeOffset.Parse("2026-07-16T09:00:00Z")
            },
            new()
            {
                Id = "article-draft",
                Title = "A draft that is not ready",
                Summary = "Publication policy should leave unfinished work in the editorial store.",
                UpdatedAt = DateTimeOffset.Parse("2026-07-17T09:00:00Z")
            }
        ];

        await articles.Save(ct);
        await articles[0].Approve(ct);
        await articles[1].Approve(ct);

        return Ok(new
        {
            total = articles.Length,
            approved = articles.Count(article => article.Status == ArticleStatus.Approved),
            drafts = articles.Count(article => article.Status == ArticleStatus.Draft),
            approvedIds = articles
                .Where(article => article.Status == ArticleStatus.Approved)
                .Select(article => article.Id)
                .Order(StringComparer.Ordinal)
                .ToArray()
        });
    }

    [HttpPost("{channel}")]
    public async Task<IActionResult> Publish(PublicationChannel channel, CancellationToken ct)
    {
        try
        {
            var result = await Article.PublishApproved(channel, ct);
            var snapshot = await ReadChannel(channel, ct);

            return Ok(new
            {
                channel,
                result.ReadCount,
                result.CopiedCount,
                result.Duration,
                result.Warnings,
                articles = snapshot
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Publication to {Channel} failed", channel);
            return Problem(
                title: $"The {channel} channel could not accept this publication.",
                detail: CorrectionFor(channel),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    [HttpGet("{channel}")]
    public async Task<IActionResult> Get(PublicationChannel channel, CancellationToken ct)
    {
        try
        {
            return Ok(await ReadChannel(channel, ct));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reading publication channel {Channel} failed", channel);
            return Problem(
                title: $"The {channel} channel is unavailable.",
                detail: CorrectionFor(channel),
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }
    }

    private static async Task<object> ReadChannel(PublicationChannel channel, CancellationToken ct)
    {
        using var scope = EntityContext.Source(channel.ToString());
        var articles = await Article.All(ct);
        var capabilities = Data<Article, string>.Capabilities.All
            .Select(capability => capability.Id)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new
        {
            channel,
            count = articles.Count,
            ids = articles.Select(article => article.Id).Order(StringComparer.Ordinal).ToArray(),
            articles,
            capabilities
        };
    }

    private static string CorrectionFor(PublicationChannel channel) => channel switch
    {
        PublicationChannel.Documents =>
            "Start the sample Mongo service with docker compose -f docker/compose.yml up -d mongo, then retry.",
        PublicationChannel.Relational =>
            "Start the sample Postgres service with docker compose -f docker/compose.yml up -d postgres, then retry.",
        _ => "Ensure the sample directory is writable and inspect /.well-known/Koan/facts for the selected source."
    };
}
