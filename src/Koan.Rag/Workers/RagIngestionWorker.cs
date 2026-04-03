using Koan.Rag.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Rag.Workers;

/// <summary>
/// Background service that processes queued RAG ingestion jobs.
/// Follows the <c>EmbeddingWorker</c> pattern: adaptive polling,
/// batch processing, retry with exponential backoff.
/// </summary>
internal sealed class RagIngestionWorker(
    ILogger<RagIngestionWorker> logger,
    IOptions<RagOptions> options) : BackgroundService
{
    private readonly RagOptions _config = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RagIngestionWorker started (GraphStrategy={Strategy})",
            _config.GraphStrategy);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // TODO Phase 4: Process queued RagIngestionState<T> jobs
                // For now, the worker is idle — ingestion runs synchronously
                // in the lifecycle hooks or via explicit Ingest() calls.
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "RagIngestionWorker encountered error in main loop");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        logger.LogInformation("RagIngestionWorker stopped");
    }
}
