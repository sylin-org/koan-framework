using Koan.Data.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using S5.Recs.Infrastructure;
using S5.Recs.Models;

namespace S5.Recs.Services.Workers;

/// <summary>
/// Background worker that watches the default partition for newly vectorized
/// media items and updates tag/genre catalogs.
/// Part of ARCH-0069: Partition-Based Import Pipeline Architecture.
/// </summary>
public class CatalogWorker : BackgroundService
{
    private readonly ILogger<CatalogWorker> _logger;
    private DateTimeOffset _lastProcessed = DateTimeOffset.MinValue;

    public CatalogWorker(ILogger<CatalogWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CatalogWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Query default partition for newly processed media
                // Note: VectorizedAt removed (ARCH-0070) - embeddings now automatic
                var cutoff = _lastProcessed;
                var newMedia = (await Media.Query(
                    m => m.ImportedAt > cutoff,  // Changed from VectorizedAt to ImportedAt
                    stoppingToken)).ToList();

                if (newMedia.Any())
                {
                    await CatalogTagsAsync(newMedia, stoppingToken);
                    await CatalogGenresAsync(newMedia, stoppingToken);

                    _logger.LogInformation(
                        "Cataloged {Count} newly vectorized media items",
                        newMedia.Count);

                    _lastProcessed = DateTimeOffset.UtcNow;
                }

                // Run every minute
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "CatalogWorker encountered error in main loop");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        _logger.LogInformation("CatalogWorker stopped");
    }

    private async Task CatalogTagsAsync(List<Media> items, CancellationToken ct)
    {
        // Extract tags from media items (tags + genres combined)
        var allTags = items
            .SelectMany(m => m.Tags ?? Array.Empty<string>())
            .Concat(items.SelectMany(m => m.Genres ?? Array.Empty<string>()))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .ToList();

        // Apply preemptive NSFW filter (marks, doesn't block)
        var tagStats = allTags
            .GroupBy(tag => tag.ToLowerInvariant())
            .Select(g =>
            {
                var tagName = g.First();
                return new TagStatDoc
                {
                    Id = g.Key,
                    Tag = tagName,
                    MediaCount = g.Count(),
                    IsNsfw = PreemptiveTagFilter.ShouldCensor(tagName), // Mark NSFW tags
                    UpdatedAt = DateTimeOffset.UtcNow
                };
            })
            .ToList();

        if (tagStats.Any())
        {
            await tagStats.Save(ct);

            var nsfwCount = tagStats.Count(t => t.IsNsfw);
            _logger.LogDebug(
                "Cataloged {Count} tags ({NsfwCount} marked NSFW)",
                tagStats.Count, nsfwCount);
        }
    }

    private async Task CatalogGenresAsync(List<Media> items, CancellationToken ct)
    {
        // Extract genres from media items
        var allGenres = items
            .SelectMany(m => m.Genres ?? Array.Empty<string>())
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .ToList();

        var genreStats = allGenres
            .GroupBy(g => g.ToLowerInvariant())
            .Select(g => new GenreStatDoc
            {
                Id = g.Key,
                Genre = g.First(),
                MediaCount = g.Count(),
                UpdatedAt = DateTimeOffset.UtcNow
            })
            .ToList();

        if (genreStats.Any())
        {
            await genreStats.Save(ct);

            _logger.LogDebug("Cataloged {Count} genres", genreStats.Count);
        }
    }
}
