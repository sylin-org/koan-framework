using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using S13.DocMind.Infrastructure;

namespace S13.DocMind.Services;

public sealed class DocumentProcessingHostedService : BackgroundService
{
    private readonly IDocumentPipelineQueue _queue;
    private readonly DocumentAnalysisPipeline _pipeline;
    private readonly IOptionsMonitor<DocumentPipelineQueueOptions> _queueOptions;
    private readonly ILogger<DocumentProcessingHostedService> _logger;

    public DocumentProcessingHostedService(
        IDocumentPipelineQueue queue,
        DocumentAnalysisPipeline pipeline,
        IOptionsMonitor<DocumentPipelineQueueOptions> queueOptions,
        ILogger<DocumentProcessingHostedService> logger)
    {
        _queue = queue;
        _pipeline = pipeline;
        _queueOptions = queueOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Document processing hosted service started");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var batchSize = Math.Max(1, _queueOptions.CurrentValue.WorkerBatchSize);
                var batch = await _queue.DequeueBatchAsync(batchSize, stoppingToken);
                if (batch.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                await _pipeline.ProcessBatchAsync(batch, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // graceful shutdown
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception in document processing loop");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("Document processing hosted service stopped");
    }
}
