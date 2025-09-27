using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Vector;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using S13.DocMind.Models;

namespace S13.DocMind.Infrastructure;

public sealed class DocumentVectorBootstrapper : BackgroundService
{
    private readonly ILogger<DocumentVectorBootstrapper> _logger;

    public DocumentVectorBootstrapper(ILogger<DocumentVectorBootstrapper> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!Vector<DocumentChunkEmbedding>.IsAvailable)
        {
            _logger.LogInformation("Vector provider not configured; skipping DocMind vector bootstrap");
            return;
        }

        try
        {
            await Vector<DocumentChunkEmbedding>.EnsureCreated(stoppingToken).ConfigureAwait(false);
            _logger.LogInformation("DocMind vector index ensured");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to ensure DocMind vector index");
        }
    }
}
