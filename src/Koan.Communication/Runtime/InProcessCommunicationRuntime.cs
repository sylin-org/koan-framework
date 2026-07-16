using System.Threading.Channels;
using Koan.Communication.Adapters;
using Koan.Core;
using Koan.Core.Context;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Communication.Runtime;

[ProviderPriority(int.MinValue)]
internal sealed class InProcessCommunicationRuntime : ICommunicationAdapter
{
    private static readonly CommunicationAdapterDescriptor AdapterDescriptor = new(
        "in-process",
        [CommunicationLane.Events, CommunicationLane.Transport],
        CommunicationDeliveryAssurance.ProcessMemory,
        CommunicationAdapterCapabilities.ContractIdentity
        | CommunicationAdapterCapabilities.SnapshotCopy
        | CommunicationAdapterCapabilities.ContextCarriage
        | CommunicationAdapterCapabilities.TypedGroups
        | CommunicationAdapterCapabilities.GroupFanOut
        | CommunicationAdapterCapabilities.MessageIdentity
        | CommunicationAdapterCapabilities.BoundedAcceptance
        | CommunicationAdapterCapabilities.ZeroTargetEvents,
        [],
        IsBuiltIn: true);

    private readonly Channel<LocalPublication> _events;
    private readonly Channel<LocalPublication> _transport;
    private readonly ILogger<InProcessCommunicationRuntime> _logger;
    private readonly CancellationTokenSource _abort = new();
    private CommunicationAdapterHost? _host;
    private Task? _eventsWorker;
    private Task? _transportWorker;
    private int _state;

    public InProcessCommunicationRuntime(
        IOptions<CommunicationOptions> options,
        ILogger<InProcessCommunicationRuntime> logger)
    {
        _logger = logger;
        _events = CreateLane(options.Value.InProcessCapacity);
        _transport = CreateLane(options.Value.InProcessCapacity);
    }

    public CommunicationAdapterDescriptor Descriptor => AdapterDescriptor;
    public bool IsReady => Volatile.Read(ref _state) == 1;

    public Task Start(CommunicationAdapterHost host, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("The process-local Communication provider cannot be started more than once.");

        _host = host;
        _eventsWorker = Pump(_events, CommunicationLane.Events, _abort.Token);
        _transportWorker = Pump(_transport, CommunicationLane.Transport, _abort.Token);
        return Task.CompletedTask;
    }

    public async ValueTask<CommunicationAdapterAcceptance> Publish(
        CommunicationAdapterPublication publication,
        CancellationToken ct)
    {
        EnsureAvailable();
        var targets = _host!.Bindings
            .Where(binding =>
                binding.Lane == publication.Lane
                && string.Equals(binding.Channel, publication.Channel, StringComparison.Ordinal)
                && string.Equals(binding.ContractId, publication.ContractId, StringComparison.Ordinal))
            .ToArray();
        if (publication.Lane == CommunicationLane.Transport && targets.Length == 0)
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.NoRoute,
                $"The process-local Transport provider has no receiver group for '{publication.ContractId}'.");

        await WriterFor(publication.Lane)
            .WriteAsync(new LocalPublication(publication, targets), ct)
            .ConfigureAwait(false);
        return new CommunicationAdapterAcceptance(targets.Length, SettlementObservable: true);
    }

    public async Task Stop(CancellationToken ct)
    {
        var prior = Interlocked.Exchange(ref _state, 2);
        if (prior == 0) return;

        CompleteWriters();
        var workers = Workers();
        if (workers.Length == 0) return;

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
            try { await Task.WhenAll(workers).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }

        _abort.Dispose();
    }

    private async Task Pump(
        Channel<LocalPublication> channel,
        CommunicationLane lane,
        CancellationToken ct)
    {
        try
        {
            await foreach (var work in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                foreach (var target in work.Targets)
                {
                    var outcome = await _host!.Dispatch(
                            target.Id,
                            work.Publication.Payload,
                            ContextIngressTrust.HostTrusted,
                            ct)
                        .ConfigureAwait(false);
                    Mark(work.Publication.Operation, outcome);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Host-forced stop; queued targets are failed below.
        }
        catch (Exception error)
        {
            _logger.LogCritical(error, "Koan Communication process-local {Lane} dispatcher stopped unexpectedly.", lane);
            Interlocked.Exchange(ref _state, 2);
            CompleteWriters();
        }
        finally
        {
            while (channel.Reader.TryRead(out var abandoned))
            {
                foreach (var _ in abandoned.Targets) abandoned.Publication.Operation.MarkFailed();
            }
        }
    }

    private static void Mark(CommunicationOperation operation, CommunicationDeliveryOutcome outcome)
    {
        switch (outcome)
        {
            case CommunicationDeliveryOutcome.Delivered:
                operation.MarkDelivered();
                break;
            case CommunicationDeliveryOutcome.Filtered:
                operation.MarkFiltered();
                break;
            default:
                operation.MarkFailed();
                break;
        }
    }

    private void EnsureAvailable()
    {
        if (!IsReady)
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.Unavailable,
                "The process-local Communication provider is not accepting publications. Start the Koan host first.");
    }

    private static Channel<LocalPublication> CreateLane(int capacity)
        => Channel.CreateBounded<LocalPublication>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });

    private ChannelWriter<LocalPublication> WriterFor(CommunicationLane lane)
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

    private sealed record LocalPublication(
        CommunicationAdapterPublication Publication,
        IReadOnlyList<CommunicationAdapterBinding> Targets);
}
