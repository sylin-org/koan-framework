using Koan.Data.Abstractions;
using Koan.Data.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using S5.Recs.Models;

namespace S5.Recs.Services.Workers;

/// <summary>
/// Background worker that validates media items in "import-raw" partition
/// and moves valid items to "vectorization-queue" partition.
/// Part of ARCH-0069: Partition-Based Import Pipeline Architecture.
/// </summary>
public class ValidationWorker : BackgroundService
{
    private readonly ILogger<ValidationWorker> _logger;

    public ValidationWorker(ILogger<ValidationWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ValidationWorker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Poll "import-raw" partition for media items to validate
                List<Media> batch;
                using (EntityContext.Partition("import-raw"))
                {
                    var result = await Media.All(
                        new DataQueryOptions { PageSize = 50 },
                        stoppingToken);
                    batch = result.ToList();
                }

                if (batch.Any())
                {
                    await ProcessBatchAsync(batch, stoppingToken);
                }
                else
                {
                    // No items to validate - wait before polling again
                    await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "ValidationWorker encountered error in main loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("ValidationWorker stopped");
    }

    private async Task ProcessBatchAsync(List<Media> batch, CancellationToken ct)
    {
        foreach (var media in batch)
        {
            try
            {
                // Validation checks
                var validationErrors = new List<string>();

                // Check required fields
                if (string.IsNullOrEmpty(media.Title) &&
                    string.IsNullOrEmpty(media.TitleEnglish) &&
                    string.IsNullOrEmpty(media.TitleRomaji) &&
                    string.IsNullOrEmpty(media.TitleNative))
                {
                    validationErrors.Add("No title provided");
                }

                // ContentSignature validation removed - now tracked automatically by framework (ARCH-0070)

                if (string.IsNullOrEmpty(media.MediaTypeId))
                {
                    validationErrors.Add("MediaTypeId is null");
                }

                if (string.IsNullOrEmpty(media.ProviderCode))
                {
                    validationErrors.Add("ProviderCode is null");
                }

                if (string.IsNullOrEmpty(media.ExternalId))
                {
                    validationErrors.Add("ExternalId is null");
                }

                if (validationErrors.Any())
                {
                    _logger.LogWarning(
                        "Media {Id} failed validation: {Errors} - discarding",
                        media.Id, string.Join(", ", validationErrors));

                    // Remove invalid media from import-raw
                    using (EntityContext.Partition("import-raw"))
                    {
                        await Media.Remove(media.Id!, ct);
                    }

                    continue;
                }

                // Validation passed
                media.ValidatedAt = DateTimeOffset.UtcNow;

                // Update in import-raw partition
                using (EntityContext.Partition("import-raw"))
                {
                    await media.Save(ct);
                }

                // Move to vectorization-queue partition
                await Media.Copy(m => m.Id == media.Id)
                    .From(partition: "import-raw")
                    .To(partition: "vectorization-queue")
                    .Run(ct);

                // Remove from import-raw
                using (EntityContext.Partition("import-raw"))
                {
                    await Media.Remove(media.Id!, ct);
                }

                _logger.LogDebug(
                    "Validated {MediaId}, moved to vectorization-queue",
                    media.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to validate media {MediaId}: {Error}",
                    media.Id, ex.Message);

                // Remove problematic item
                try
                {
                    using (EntityContext.Partition("import-raw"))
                    {
                        await Media.Remove(media.Id!, ct);
                    }
                }
                catch (Exception removeEx)
                {
                    _logger.LogError(removeEx,
                        "Failed to remove problematic media {MediaId}",
                        media.Id);
                }
            }
        }
    }
}
