using System.Collections.Concurrent;
using System.Threading.Channels;
using Koan.Web.Sse.Infrastructure;
using Koan.Web.Sse.Options;
using Microsoft.Extensions.Options;

namespace Koan.Web.Sse;

/// <summary>
/// The open streams of one kind of value, in this process. <see cref="Open"/> admits a stream for a reader, or refuses it
/// beyond <see cref="SseHubOptions.PerReader"/> of theirs or <see cref="SseHubOptions.InAll"/> in all. <see cref="Publish"/>
/// queues a value for every open stream and never waits: a stream whose queue already holds
/// <see cref="SseHubOptions.QueueBound"/> values is too slow, and it is closed, its queue completed and its request
/// aborted, to reconnect and resume from <see cref="Sse.LastEventId"/>. Who may see a value is each stream's to decide,
/// and what a stream missed is the application's record to replay: the hub keeps nothing past its queues. Inject
/// <c>SseHub&lt;T&gt;</c>; one per value type per host.
/// </summary>
public sealed class SseHub<T>
{
    private readonly SseHubOptions _limits;
    private readonly ConcurrentDictionary<SseConnection<T>, byte> _open = new();
    private readonly Dictionary<string, int> _perReader = new(StringComparer.Ordinal);
    private readonly Lock _counts = new();
    private int _inAll;

    /// <exception cref="InvalidOperationException">A limit is below one.</exception>
    public SseHub(IOptions<KoanSseOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _limits = options.Value.Hub;
        Require(_limits.QueueBound, nameof(SseHubOptions.QueueBound));
        Require(_limits.PerReader, nameof(SseHubOptions.PerReader));
        Require(_limits.InAll, nameof(SseHubOptions.InAll));
    }

    /// <summary>
    /// A new stream for <paramref name="reader"/>, or null when they hold their limit or the hub is full. Key a signed-in
    /// reader by their id and a visitor by their address. <paramref name="abort"/> ends the stream's request when it is
    /// closed as too slow: pass <c>HttpContext.Abort</c>. Disposing the stream gives its place back.
    /// </summary>
    public SseConnection<T>? Open(string reader, Action abort)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(abort);

        lock (_counts)
        {
            var held = _perReader.GetValueOrDefault(reader);
            if (held >= _limits.PerReader || _inAll >= _limits.InAll) return null;
            _perReader[reader] = held + 1;
            _inAll++;
        }

        var connection = new SseConnection<T>(reader, _limits.QueueBound, abort, Close);
        _open[connection] = 0;
        return connection;
    }

    /// <summary>Queues <paramref name="value"/> for every open stream, closing each whose queue is full.</summary>
    public void Publish(T value)
    {
        // Enumerating the dictionary itself takes no lock; its Keys property would take every bucket's.
        foreach (var (connection, _) in _open) connection.Offer(value);
    }

    /// <summary>How many streams are open, in all and for one reader.</summary>
    public (int InAll, int Reader) Count(string reader)
    {
        lock (_counts) return (_inAll, _perReader.GetValueOrDefault(reader));
    }

    private void Close(SseConnection<T> connection)
    {
        if (!_open.TryRemove(connection, out _)) return;
        lock (_counts)
        {
            _inAll--;
            if (--_perReader[connection.Reader] == 0) _perReader.Remove(connection.Reader);
        }
    }

    private static void Require(int limit, string key)
    {
        if (limit < 1)
        {
            throw new InvalidOperationException(
                $"{Constants.Configuration.Section}:Hub:{key} sizes what an SSE hub holds; it must be at least 1. It is {limit}.");
        }
    }
}

/// <summary>One open stream's queue of published values, in the order they were published.</summary>
public sealed class SseConnection<T> : IDisposable
{
    private readonly Action _abort;
    private readonly Action<SseConnection<T>> _close;
    private readonly Channel<T> _queue;
    private int _slow;
    private int _closed;

    internal SseConnection(string reader, int queueBound, Action abort, Action<SseConnection<T>> close)
    {
        Reader = reader;
        _abort = abort;
        _close = close;
        _queue = Channel.CreateBounded<T>(new BoundedChannelOptions(queueBound)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
    }

    public string Reader { get; }

    /// <summary>Whether the stream was closed because its queue was full.</summary>
    public bool Slow => Volatile.Read(ref _slow) == 1;

    /// <summary>The values published since it opened; it completes when the stream is closed.</summary>
    public ChannelReader<T> Queue => _queue.Reader;

    internal void Offer(T value)
    {
        if (Volatile.Read(ref _closed) == 1 || _queue.Writer.TryWrite(value)) return;
        if (Interlocked.Exchange(ref _closed, 1) == 1) return;
        Volatile.Write(ref _slow, 1);
        _queue.Writer.TryComplete();
        _close(this);
        try
        {
            _abort();
        }
        catch (ObjectDisposedException)
        {
            // The request has already ended.
        }
    }

    public void Dispose()
    {
        Volatile.Write(ref _closed, 1);
        _queue.Writer.TryComplete();
        _close(this);
    }
}
