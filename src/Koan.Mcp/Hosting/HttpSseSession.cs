using System;
using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Koan.Mcp.Hosting;

public sealed class HttpSseSession : IDisposable
{
    private readonly Channel<ServerSentEvent> _outbound;
    private readonly TimeProvider _timeProvider;
    private HttpSseRpcBridge? _bridge;

    internal HttpSseSession(
        string id,
        ClaimsPrincipal user,
        CancellationTokenSource cancellation,
        TimeProvider timeProvider,
        DateTimeOffset createdAtUtc)
    {
        Id = id ?? throw new ArgumentNullException(nameof(id));
        User = user ?? new ClaimsPrincipal();
        Cancellation = cancellation ?? throw new ArgumentNullException(nameof(cancellation));
        _timeProvider = timeProvider ?? TimeProvider.System;
        CreatedAtUtc = createdAtUtc;
        LastActivityUtc = createdAtUtc;
        _outbound = Channel.CreateUnbounded<ServerSentEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            AllowSynchronousContinuations = false
        });
    }

    public string Id { get; }

    public ClaimsPrincipal User { get; }

    public CancellationTokenSource Cancellation { get; }

    public DateTimeOffset CreatedAtUtc { get; }

    public DateTimeOffset LastActivityUtc { get; private set; }

    public void AttachBridge(HttpSseRpcBridge bridge)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    public bool TryGetBridge([NotNullWhen(true)] out HttpSseRpcBridge? bridge)
    {
        bridge = _bridge;
        return bridge is not null;
    }

    public HttpSseRpcBridge Bridge => _bridge ?? throw new InvalidOperationException("Session bridge not initialised.");

    public void Enqueue(ServerSentEvent message)
    {
        LastActivityUtc = _timeProvider.GetUtcNow();
        _outbound.Writer.TryWrite(message);
    }

    public IAsyncEnumerable<ServerSentEvent> OutboundMessages(CancellationToken cancellationToken)
        => _outbound.Reader.ReadAllAsync(cancellationToken);

    public void Complete()
    {
        _outbound.Writer.TryComplete();
    }

    public void Dispose()
    {
        Cancellation.Dispose();
    }
}
