using System.Threading.Channels;
using Koan.Context.Models;
using Microsoft.Extensions.Logging;

namespace Koan.Context.Services;

public interface IIndexingResumptionQueue
{
    ValueTask EnqueueAsync(IndexingResumptionRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<IndexingResumptionRequest> ReadAllAsync(CancellationToken cancellationToken);
}

public sealed record IndexingResumptionRequest(string ProjectId, JobStatus PreviousStatus, TimeSpan Delay);

public sealed class IndexingResumptionQueue : IIndexingResumptionQueue
{
    private readonly Channel<IndexingResumptionRequest> _channel;
    private readonly ILogger<IndexingResumptionQueue> _logger;

    public IndexingResumptionQueue(ILogger<IndexingResumptionQueue> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var options = new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };
        _channel = Channel.CreateUnbounded<IndexingResumptionRequest>(options);
    }

    public ValueTask EnqueueAsync(IndexingResumptionRequest request, CancellationToken cancellationToken)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        if (_channel.Writer.TryWrite(request))
        {
            _logger.LogDebug(
                "Queued indexing resume for project {ProjectId} with delay {DelaySeconds}s",
                request.ProjectId,
                request.Delay.TotalSeconds);
            return ValueTask.CompletedTask;
        }

        return WriteAsyncWithLog(request, cancellationToken);
    }

    public IAsyncEnumerable<IndexingResumptionRequest> ReadAllAsync(CancellationToken cancellationToken)
    {
        return _channel.Reader.ReadAllAsync(cancellationToken);
    }

    private async ValueTask WriteAsyncWithLog(IndexingResumptionRequest request, CancellationToken cancellationToken)
    {
        await _channel.Writer.WriteAsync(request, cancellationToken);
        _logger.LogDebug(
            "Queued indexing resume for project {ProjectId} with delay {DelaySeconds}s",
            request.ProjectId,
            request.Delay.TotalSeconds);
    }
}
