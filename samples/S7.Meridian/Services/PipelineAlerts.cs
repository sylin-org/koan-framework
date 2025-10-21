using Koan.Samples.Meridian.Models;
using Microsoft.Extensions.Logging;

namespace Koan.Samples.Meridian.Services;

public interface IPipelineAlertService
{
    Task PublishWarning(string pipelineId, string code, string message, CancellationToken ct);
}

public sealed class PipelineAlertService : IPipelineAlertService
{
    private readonly ILogger<PipelineAlertService> _logger;

    public PipelineAlertService(ILogger<PipelineAlertService> logger)
    {
        _logger = logger;
    }

    public Task PublishWarning(string pipelineId, string code, string message, CancellationToken ct)
    {
        _logger.LogWarning("Pipeline {PipelineId} warning {Code}: {Message}", pipelineId, code, message);
        return Task.CompletedTask;
    }
}
