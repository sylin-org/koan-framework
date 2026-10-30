using System.Threading.RateLimiting;

namespace Koan.Communication.Connector.RabbitMq.Publishing;

/// <summary>
/// Bounds the publications awaiting a broker confirm. A permit is held around the whole awaited publish, which returns
/// only once its confirm settles, and is released on every outcome. Up to <c>limit</c> publications await their
/// confirms at once with no delay; a further one waits, oldest first, until a confirm settles, so the memory a slow
/// broker can make the publisher hold is bounded. RabbitMQ.Client's own outstanding-confirmation limiter cannot give
/// this bound: it releases its lease once the frame is written, before the confirm is awaited.
/// </summary>
internal sealed class PublisherConfirmWindow(int limit) : IAsyncDisposable
{
    private readonly ConcurrencyLimiter _permits = new(new ConcurrencyLimiterOptions
    {
        PermitLimit = limit,
        QueueLimit = int.MaxValue,
        QueueProcessingOrder = QueueProcessingOrder.OldestFirst
    });

    public async Task Publish(Func<ValueTask> publish, CancellationToken ct)
    {
        using var permit = await _permits.AcquireAsync(1, ct).ConfigureAwait(false);
        if (!permit.IsAcquired)
            throw new InvalidOperationException("The RabbitMQ publisher confirm window refused a permit.");
        await publish().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => _permits.DisposeAsync();
}
