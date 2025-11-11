using Koan.Context.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

internal sealed class IndexingResumptionWorker : BackgroundService
{
    private readonly IIndexingResumptionQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IndexingResumptionWorker> _logger;

    public IndexingResumptionWorker(
        IIndexingResumptionQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<IndexingResumptionWorker> logger)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                if (request.Delay > TimeSpan.Zero)
                {
                    _logger.LogInformation(
                        "Delaying indexing resume for project {ProjectId} by {DelaySeconds}s",
                        request.ProjectId,
                        request.Delay.TotalSeconds);
                    await Task.Delay(request.Delay, stoppingToken);
                }

                using var scope = _scopeFactory.CreateScope();
                var indexer = scope.ServiceProvider.GetRequiredService<Indexer>();

                var project = await Project.Get(request.ProjectId, stoppingToken);
                if (project is null)
                {
                    _logger.LogWarning(
                        "Skipping indexing resume for project {ProjectId} because it no longer exists",
                        request.ProjectId);
                    continue;
                }

                _logger.LogInformation(
                    "Resuming indexing for project {ProjectId} (previous status {PreviousStatus})",
                    request.ProjectId,
                    request.PreviousStatus);

                await indexer.IndexProjectAsync(
                    request.ProjectId,
                    progress: null,
                    cancellationToken: stoppingToken,
                    force: false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Indexing resumption worker stopping due to cancellation");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to resume indexing for project {ProjectId}",
                    request.ProjectId);
            }
        }
    }
}
