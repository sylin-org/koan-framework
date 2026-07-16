using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Communication.Runtime;

internal sealed class InProcessTransportRuntime : IAsyncDisposable
{
    private readonly Channel<TransportEnvelope> _channel;
    private readonly TransportIngress _ingress;
    private readonly ILogger<InProcessTransportRuntime> _logger;
    private readonly CancellationTokenSource _abort = new();
    private Task? _worker;
    private int _state;

    public InProcessTransportRuntime(
        IOptions<CommunicationOptions> options,
        TransportIngress ingress,
        ILogger<InProcessTransportRuntime> logger)
    {
        _ingress = ingress;
        _logger = logger;
        _channel = Channel.CreateBounded<TransportEnvelope>(new BoundedChannelOptions(options.Value.InProcessCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            throw new InvalidOperationException("The process-local Transport runtime cannot be started more than once.");
        }

        _worker = Pump(_abort.Token);
    }

    public void EnsureAvailable()
    {
        if (Volatile.Read(ref _state) != 1)
        {
            throw new InvalidOperationException(
                "The process-local Transport runtime is not accepting publications. " +
                "Start the Koan host before calling Entity.Transport.Send().");
        }
    }

    public ValueTask Accept(TransportEnvelope envelope, CancellationToken ct)
    {
        EnsureAvailable();
        return _channel.Writer.WriteAsync(envelope, ct);
    }

    public async Task Stop(CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _state, 2, 1) != 1)
        {
            return;
        }

        _channel.Writer.TryComplete();
        if (_worker is null)
        {
            return;
        }

        try
        {
            await _worker.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _abort.Cancel();
            await _worker.ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        _abort.Cancel();
        if (_worker is not null)
        {
            try
            {
                await _worker.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected only when host disposal interrupts a non-drained queue.
            }
        }

        _abort.Dispose();
    }

    private async Task Pump(CancellationToken ct)
    {
        try
        {
            await foreach (var envelope in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                await Dispatch(envelope, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Disposal or a host-forced stop; queued targets are failed below.
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Koan Communication process-local Transport dispatcher stopped unexpectedly.");
        }
        finally
        {
            Interlocked.Exchange(ref _state, 2);
            while (_channel.Reader.TryRead(out var abandoned))
            {
                foreach (var _ in abandoned.Receivers)
                {
                    abandoned.Operation.MarkFailed();
                }
            }
        }
    }

    private async Task Dispatch(TransportEnvelope envelope, CancellationToken ct)
    {
        foreach (var receiver in envelope.Receivers)
        {
            try
            {
                var outcome = await _ingress.Dispatch(receiver, envelope, ct).ConfigureAwait(false);
                if (outcome == TransportTargetOutcome.Filtered)
                {
                    envelope.Operation.MarkFiltered();
                }
                else
                {
                    envelope.Operation.MarkDelivered();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                envelope.Operation.MarkFailed();
            }
            catch (Exception ex)
            {
                envelope.Operation.MarkFailed();
                _logger.LogError(
                    ex,
                    "Entity Transport receiver {ReceiverGroup} failed for {EntityType} in operation {OperationId} at ordinal {Ordinal}.",
                    receiver.GroupIdentity,
                    envelope.EntityType.FullName,
                    envelope.Operation.OperationId,
                    envelope.Ordinal);
            }
        }
    }
}
