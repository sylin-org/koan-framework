using System.Text;
using System.Threading.Channels;
using Koan.Communication.Adapters;
using Koan.Communication.Runtime;
using Koan.Core.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Communication.Signals;

/// <summary>
/// Host-owned, bounded egress for lossy framework hints. Callers only learn whether the hint entered the local
/// bounded queue; the selected Communication provider owns physical delivery and health.
/// </summary>
internal sealed class FrameworkSignalRuntime : IFrameworkSignalPublisher, IAsyncDisposable
{
    private readonly CommunicationRouter _router;
    private readonly CommunicationRouteDecision _signalRoute;
    private readonly CommunicationRouteDecision _broadcastRoute;
    private readonly CommunicationOptions _options;
    private readonly ILogger<FrameworkSignalRuntime> _logger;
    private readonly Channel<SignalPublication> _outbound;
    private readonly CancellationTokenSource _abort = new();
    private Task? _pump;
    private int _state;

    public FrameworkSignalRuntime(
        CommunicationRouter router,
        IOptions<CommunicationOptions> options,
        ILogger<FrameworkSignalRuntime> logger)
    {
        _router = router;
        _signalRoute = router.For(CommunicationLane.FrameworkSignals);
        _broadcastRoute = router.For(CommunicationLane.FrameworkBroadcasts);
        _options = options.Value;
        _logger = logger;
        _outbound = Channel.CreateBounded<SignalPublication>(new BoundedChannelOptions(_options.InProcessCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public string ProviderId => _signalRoute.AdapterId;
    public string Assurance => _signalRoute.Assurance;
    public string BroadcastProviderId => _broadcastRoute.AdapterId;
    public string BroadcastAssurance => _broadcastRoute.Assurance;
    public bool BroadcastIsBuiltIn => _broadcastRoute.Adapter.Descriptor.IsBuiltIn;

    public bool TryPublish<TSignal>(TSignal signal)
        where TSignal : struct, IFrameworkSignal<TSignal>
        => TryQueue(signal, TSignal.ContractId, CommunicationLane.FrameworkSignals, _signalRoute);

    public bool TryBroadcast<TSignal>(TSignal signal)
        where TSignal : struct, IFrameworkBroadcast<TSignal>
        => TryQueue(signal, TSignal.ContractId, CommunicationLane.FrameworkBroadcasts, _broadcastRoute);

    private bool TryQueue<TSignal>(
        TSignal signal,
        string contractId,
        CommunicationLane lane,
        CommunicationRouteDecision route)
        where TSignal : struct
    {
        if (Volatile.Read(ref _state) != 1) return false;

        try
        {
            var payload = signal.ToJson();
            if (Encoding.UTF8.GetByteCount(payload) > _options.MaxPayloadBytes)
            {
                _logger.LogDebug(
                    "Koan Communication dropped oversized framework signal {Contract}; the owning subsystem fallback remains active.",
                    contractId);
                return false;
            }

            var operation = new CommunicationOperation(route);
            operation.MarkEnumerated();
            var wire = new CommunicationWireEnvelope(
                CommunicationWireCodec.SchemaVersion,
                _router.MeshId,
                lane,
                route.Channel,
                contractId,
                operation.OperationId,
                Ordinal: 0,
                payload,
                Context: null);
            var publication = new SignalPublication(
                contractId,
                route,
                operation.OperationId.ToString("N"),
                CommunicationWireCodec.Encode(wire),
                operation);
            if (_outbound.Writer.TryWrite(publication)) return true;

            operation.MarkRejected();
            operation.Seal(false);
            _logger.LogDebug(
                "Koan Communication dropped framework signal {Contract} because its bounded egress queue is full; " +
                "the owning subsystem fallback remains active.",
                contractId);
            return false;
        }
        catch (Exception error)
        {
            _logger.LogDebug(
                error,
                "Koan Communication could not encode framework signal {Contract}; the owning subsystem fallback remains active.",
                contractId);
            return false;
        }
    }

    public Task Start(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("The framework-signal runtime cannot be started more than once.");
        _pump = Pump(_abort.Token);
        return Task.CompletedTask;
    }

    public async Task Stop(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _state, 2) != 1) return;
        _outbound.Writer.TryComplete();
        if (_pump is null) return;

        try
        {
            await _pump.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _abort.Cancel();
            await _pump.ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _state, 2);
        _outbound.Writer.TryComplete();
        _abort.Cancel();
        if (_pump is not null)
        {
            try { await _pump.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _abort.Dispose();
    }

    private async Task Pump(CancellationToken ct)
    {
        try
        {
            await foreach (var publication in _outbound.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    var accepted = await _router.Publish(
                            publication.Route,
                            publication.ContractId,
                            publication.MessageId,
                            publication.Payload,
                            publication.Operation,
                            ct)
                        .ConfigureAwait(false);
                    publication.Operation.ReserveAcceptance(
                        accepted.TargetGroups,
                        accepted.SettlementObservable);
                    publication.Operation.Seal(true);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    publication.Operation.MarkRejected();
                    publication.Operation.Seal(false);
                    throw;
                }
                catch (Exception error)
                {
                    publication.Operation.MarkRejected();
                    publication.Operation.Seal(false);
                    _logger.LogDebug(
                        error,
                        "Koan Communication could not publish framework signal {Contract}; the owning subsystem fallback remains active.",
                        publication.ContractId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            while (_outbound.Reader.TryRead(out var abandoned))
            {
                abandoned.Operation.MarkRejected();
                abandoned.Operation.Seal(false);
            }
        }
    }

    private sealed record SignalPublication(
        string ContractId,
        CommunicationRouteDecision Route,
        string MessageId,
        ReadOnlyMemory<byte> Payload,
        CommunicationOperation Operation);
}
