using System.Threading.Channels;
using Microsoft.Extensions.Options;
using S13.DocMind.Models;

namespace S13.DocMind.Infrastructure;

public sealed class DocumentPipelineQueue
{
    private readonly Channel<DocumentWorkItem> _channel;

    public DocumentPipelineQueue(IOptions<DocMindProcessingOptions> options)
    {
        var opts = options.Value;
        var capacity = Math.Max(8, opts.QueueCapacity);
        _channel = Channel.CreateBounded<DocumentWorkItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public ValueTask EnqueueAsync(DocumentWorkItem workItem, CancellationToken cancellationToken)
        => _channel.Writer.WriteAsync(workItem, cancellationToken);

    public bool TryEnqueue(DocumentWorkItem workItem) => _channel.Writer.TryWrite(workItem);

    public IAsyncEnumerable<DocumentWorkItem> DequeueAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);
}

public readonly record struct DocumentWorkItem(string DocumentId, DocumentProcessingStage Stage = DocumentProcessingStage.Upload);
