using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Communication.Runtime;

internal sealed class InProcessCommunicationRuntime : IAsyncDisposable
{
    private readonly Channel<CommunicationEnvelope> _events;
    private readonly Channel<CommunicationEnvelope> _transport;
    private readonly CommunicationIngress _ingress;
    private readonly ILogger<InProcessCommunicationRuntime> _logger;
    private readonly CancellationTokenSource _abort = new();
    private Task? _eventsWorker;
    private Task? _transportWorker;
    private int _state;

    public InProcessCommunicationRuntime(
        IOptions<CommunicationOptions> options,
        CommunicationIngress ingress,
        ILogger<InProcessCommunicationRuntime> logger)
    {
        _ingress = ingress;
        _logger = logger;
        _events = CreateLane(options.Value.InProcessCapacity);
        _transport = CreateLane(options.Value.InProcessCapacity);
    }

    public void Start()
    {
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
        {
            throw new InvalidOperationException("The process-local Communication runtime cannot be started more than once.");
        }

        _eventsWorker = Pump(_events, CommunicationLane.Events, _abort.Token);
        _transportWorker = Pump(_transport, CommunicationLane.Transport, _abort.Token);
    }

    public void EnsureAvailable()
    {
        if (Volatile.Read(ref _state) != 1)
        {
            throw new InvalidOperationException(
                "The process-local Communication runtime is not accepting publications. " +
                "Start the Koan host before raising Events or sending Entity Transport snapshots.");
        }
    }

    public ValueTask Accept(CommunicationEnvelope envelope, CancellationToken ct)
    {
        EnsureAvailable();
        return WriterFor(envelope.Lane).WriteAsync(envelope, ct);
    }

    public async Task Stop(CancellationToken ct)
    {
        var prior = Interlocked.Exchange(ref _state, 2);
        if (prior == 0)
        {
            return;
        }

        CompleteWriters();
        var workers = Workers();
        if (workers.Length == 0)
        {
            return;
        }

        try
        {
            await Task.WhenAll(workers).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _abort.Cancel();
            await Task.WhenAll(workers).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _state, 2);
        CompleteWriters();
        _abort.Cancel();
        var workers = Workers();
        if (workers.Length > 0)
        {
            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected only when host disposal interrupts non-drained lanes.
            }
        }

        _abort.Dispose();
    }

    private async Task Pump(
        Channel<CommunicationEnvelope> channel,
        CommunicationLane lane,
        CancellationToken ct)
    {
        try
        {
            await foreach (var envelope in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
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
            _logger.LogCritical(
                ex,
                "Koan Communication process-local {Lane} dispatcher stopped unexpectedly.",
                lane);
            Interlocked.Exchange(ref _state, 2);
            CompleteWriters();
        }
        finally
        {
            while (channel.Reader.TryRead(out var abandoned))
            {
                foreach (var _ in abandoned.Targets)
                {
                    abandoned.Operation.MarkFailed();
                }
            }
        }
    }

    private async Task Dispatch(CommunicationEnvelope envelope, CancellationToken ct)
    {
        foreach (var target in envelope.Targets)
        {
            try
            {
                var outcome = await _ingress.Dispatch(target, envelope, ct).ConfigureAwait(false);
                if (outcome == CommunicationTargetOutcome.Filtered)
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
                    "Entity {Lane} target {TargetGroup} failed for {EntityType} in operation {OperationId} at ordinal {Ordinal}.",
                    envelope.Lane,
                    target.GroupIdentity,
                    envelope.EntityType.FullName,
                    envelope.Operation.OperationId,
                    envelope.Ordinal);
            }
        }
    }

    private static Channel<CommunicationEnvelope> CreateLane(int capacity)
        => Channel.CreateBounded<CommunicationEnvelope>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private ChannelWriter<CommunicationEnvelope> WriterFor(CommunicationLane lane)
        => lane == CommunicationLane.Events ? _events.Writer : _transport.Writer;

    private void CompleteWriters()
    {
        _events.Writer.TryComplete();
        _transport.Writer.TryComplete();
    }

    private Task[] Workers()
        => new[] { _eventsWorker, _transportWorker }
            .Where(static worker => worker is not null)
            .Cast<Task>()
            .ToArray();
}
