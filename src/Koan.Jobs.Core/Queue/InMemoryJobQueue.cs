using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Koan.Jobs.Queue;

internal sealed class InMemoryJobQueue : IJobQueue
{
    private readonly Channel<JobQueueItem> _channel;

    public InMemoryJobQueue()
    {
        _channel = Channel.CreateUnbounded<JobQueueItem>(new UnboundedChannelOptions
        {
            AllowSynchronousContinuations = false,
            SingleReader = false,
            SingleWriter = false
        });
    }

    public ValueTask EnqueueAsync(JobQueueItem item, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(item, cancellationToken);

    public async IAsyncEnumerable<JobQueueItem> ReadAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        while (await _channel.Reader.WaitToReadAsync(cancellationToken))
        {
            while (_channel.Reader.TryRead(out var item))
            {
                yield return item;
            }
        }
    }
}
