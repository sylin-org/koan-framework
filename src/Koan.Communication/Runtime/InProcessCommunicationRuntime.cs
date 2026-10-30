using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Koan.Communication.Adapters;
using Koan.Communication.Infrastructure;
using Koan.Core;
using Koan.Core.Context;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Koan.Communication.Runtime;

/// <summary>
/// The built-in process-local provider. Every bound receiver, subscription, or framework group owns one backlog and
/// one sequential consumer, so a slow or hung handler holds only its own group and each group receives its
/// deliveries in publication order. Publication never waits. A group's backlog is bounded by memory, never by a
/// count: a handler whose backlog stays within <see cref="CommunicationOptions.InProcessBacklogBytes"/> never loses a
/// delivery, and a group whose waiting publications reach it fails further deliveries visibly, in settlement and the
/// log, while the publisher and every other group carry on.
/// </summary>
[ProviderPriority(int.MinValue)]
internal sealed class InProcessCommunicationRuntime : ICommunicationAdapter
{
    private static readonly CommunicationAdapterDescriptor AdapterDescriptor = new(
        "in-process",
        [
            CommunicationLane.Events,
            CommunicationLane.Transport,
            CommunicationLane.FrameworkSignals,
            CommunicationLane.FrameworkBroadcasts
        ],
        CommunicationDeliveryAssurance.ProcessMemory,
        CommunicationAdapterCapabilities.ContractIdentity
        | CommunicationAdapterCapabilities.SnapshotCopy
        | CommunicationAdapterCapabilities.ContextCarriage
        | CommunicationAdapterCapabilities.TypedGroups
        | CommunicationAdapterCapabilities.GroupFanOut
        | CommunicationAdapterCapabilities.MessageIdentity
        | CommunicationAdapterCapabilities.BoundedAcceptance
        | CommunicationAdapterCapabilities.ZeroTargetEvents
        | CommunicationAdapterCapabilities.NodeFanOut,
        [],
        IsBuiltIn: true,
        SettlementObservable: true,
        IngressTrust: ContextIngressTrust.HostTrusted);

    private readonly long _backlogBytes;
    private readonly ILogger<InProcessCommunicationRuntime> _logger;
    private readonly CancellationTokenSource _abort = new();
    private IReadOnlyDictionary<RouteKey, LocalBinding[]> _routes = new Dictionary<RouteKey, LocalBinding[]>();
    private LocalBinding[] _bindings = [];
    private CommunicationAdapterHost? _host;
    private int _state;

    public InProcessCommunicationRuntime(
        IOptions<CommunicationOptions> options,
        ILogger<InProcessCommunicationRuntime> logger)
    {
        _backlogBytes = options.Value.InProcessBacklogBytes;
        _logger = logger;
    }

    public CommunicationAdapterDescriptor Descriptor => AdapterDescriptor;
    public bool IsReady => Volatile.Read(ref _state) == 1;

    public Task Start(CommunicationAdapterHost host, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Interlocked.CompareExchange(ref _state, 3, 0) != 0)
            throw new InvalidOperationException("The process-local Communication provider cannot be started more than once.");

        _host = host;
        _bindings = host.Bindings
            .Select(binding => new LocalBinding(binding, _backlogBytes, _logger))
            .ToArray();
        _routes = _bindings
            .GroupBy(static binding => new RouteKey(
                binding.Declaration.Lane,
                binding.Declaration.Channel,
                binding.Declaration.ContractId))
            .ToDictionary(static group => group.Key, static group => group.ToArray());
        foreach (var binding in _bindings)
            binding.Consumer = Consume(binding, _abort.Token);

        Volatile.Write(ref _state, 1);
        return Task.CompletedTask;
    }

    public ValueTask<CommunicationAdapterAcceptance> Publish(
        CommunicationAdapterPublication publication,
        CancellationToken ct)
    {
        EnsureAvailable();
        ct.ThrowIfCancellationRequested();
        var targets = _routes.TryGetValue(
            new RouteKey(publication.Lane, publication.Channel, publication.ContractId),
            out var bound)
            ? bound
            : [];
        if (publication.Lane != CommunicationLane.Events && targets.Length == 0)
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.NoRoute,
                $"The process-local {publication.Lane} provider has no receiver group for '{publication.ContractId}'.");

        foreach (var target in targets)
            target.Offer(publication);
        return ValueTask.FromResult(new CommunicationAdapterAcceptance(targets.Length, SettlementObservable: true));
    }

    public async Task Stop(CancellationToken ct)
    {
        var prior = Interlocked.Exchange(ref _state, 2);
        if (prior == 0) return;

        CloseBindings();
        var consumers = Consumers();
        if (consumers.Length == 0) return;

        try
        {
            await Task.WhenAll(consumers).WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            _abort.Cancel();
            await Task.WhenAll(consumers).ConfigureAwait(false);
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _state, 2);
        CloseBindings();
        _abort.Cancel();
        var consumers = Consumers();
        if (consumers.Length > 0)
            await Task.WhenAll(consumers).ConfigureAwait(false);

        _abort.Dispose();
    }

    private async Task Consume(LocalBinding binding, CancellationToken abort)
    {
        try
        {
            while (await binding.Reader.WaitToReadAsync(abort).ConfigureAwait(false))
            {
                while (!abort.IsCancellationRequested && binding.TryTake(out var delivery))
                {
                    var outcome = await Deliver(binding, delivery, abort).ConfigureAwait(false);
                    Mark(delivery.Operation, outcome);
                }
            }
        }
        catch (OperationCanceledException) when (abort.IsCancellationRequested)
        {
            // Host-forced stop; waiting deliveries are failed below.
        }
        catch (Exception error)
        {
            _logger.LogCritical(
                error,
                "Koan Communication process-local binding {BindingId} stopped unexpectedly; its deliveries fail in " +
                "settlement while every other binding continues.",
                binding.Declaration.Id);
        }
        finally
        {
            binding.Close();
            while (binding.TryTake(out var abandoned)) abandoned.Operation.MarkFailed();
        }
    }

    /// <summary>
    /// Runs one handler invocation off the consumer's thread and waits for it. The host's dispatch applies the
    /// handler timeout; the binding takes its next delivery only once this one returns, so a handler that ignores
    /// cancellation holds its own binding and no other. Because the handler never runs on the consumer's thread, a
    /// host-forced stop always reaches the wait below and abandons a handler that blocks, its delivery failed.
    /// </summary>
    private async Task<CommunicationDeliveryOutcome> Deliver(
        LocalBinding binding,
        LocalDelivery delivery,
        CancellationToken abort)
    {
        var host = _host!;
        var handling = Task.Run(() => host.Dispatch(binding.Declaration.Id, delivery.Payload, abort), abort);
        try
        {
            return await handling.WaitAsync(abort).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (abort.IsCancellationRequested)
        {
            if (!handling.IsCompleted)
            {
                _logger.LogWarning(
                    "Koan Communication stopped waiting for binding {BindingId}, whose handler did not honor the " +
                    "host's cancellation; its delivery failed.",
                    binding.Declaration.Id);
            }

            return CommunicationDeliveryOutcome.Failed;
        }
        catch (Exception error)
        {
            _logger.LogError(
                error,
                "Koan Communication dispatch for binding {BindingId} failed outside its handler; the delivery failed.",
                binding.Declaration.Id);
            return CommunicationDeliveryOutcome.Failed;
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

    private void CloseBindings()
    {
        foreach (var binding in _bindings) binding.Close();
    }

    private Task[] Consumers() => _bindings.Select(static binding => binding.Consumer).ToArray();

    private readonly record struct RouteKey(CommunicationLane Lane, string Channel, string ContractId);

    private sealed record LocalDelivery(ReadOnlyMemory<byte> Payload, CommunicationOperation Operation);

    /// <summary>
    /// One bound group's backlog. It counts the encoded bytes of every publication waiting in it; a publication shared
    /// by several groups counts once in each, because each group's backlog keeps it alive. A delivery is admitted
    /// while the waiting bytes are below the budget, so the backlog exceeds the budget by at most one publication, plus
    /// per-delivery bookkeeping and the one delivery in flight, which are not counted; a group with nothing waiting
    /// always admits. Every offered delivery settles exactly once: by the consumer, or
    /// here. Admission and the overflow episode are decided under one lock, so each episode logs one warning when it
    /// begins and one note, with its true count, when the group admits again.
    /// </summary>
    private sealed class LocalBinding(CommunicationAdapterBinding declaration, long budget, ILogger logger)
    {
        private readonly Channel<LocalDelivery> _queue = Channel.CreateUnbounded<LocalDelivery>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });

        private readonly Lock _admission = new();
        private long _waitingBytes;
        private long _episodeOverflowed;
        private bool _closed;

        public CommunicationAdapterBinding Declaration { get; } = declaration;
        public ChannelReader<LocalDelivery> Reader => _queue.Reader;
        public Task Consumer { get; set; } = Task.CompletedTask;

        public void Offer(CommunicationAdapterPublication publication)
        {
            var size = publication.Payload.Length;
            Admission admission;
            long waiting;
            long recovered = 0;
            lock (_admission)
            {
                waiting = Volatile.Read(ref _waitingBytes);
                if (_closed)
                {
                    admission = Admission.Closed;
                }
                else if (waiting >= budget)
                {
                    admission = _episodeOverflowed++ == 0 ? Admission.OverflowBegins : Admission.Overflow;
                }
                else if (_queue.Writer.TryWrite(new LocalDelivery(publication.Payload, publication.Operation)))
                {
                    Interlocked.Add(ref _waitingBytes, size);
                    recovered = _episodeOverflowed;
                    _episodeOverflowed = 0;
                    admission = Admission.Accepted;
                }
                else
                {
                    admission = Admission.Closed;
                }
            }

            switch (admission)
            {
                case Admission.Accepted:
                    if (recovered > 0)
                    {
                        logger.LogInformation(
                            "Koan Communication binding {BindingId} admits deliveries again; {Overflowed} failed in " +
                            "settlement while its backlog was over budget.",
                            Declaration.Id,
                            recovered);
                    }

                    break;
                case Admission.OverflowBegins:
                    publication.Operation.MarkOverflowed();
                    logger.LogWarning(
                        "Koan Communication binding {BindingId} holds {WaitingBytes} bytes of waiting publications, " +
                        "its {BacklogBytes}-byte budget ({Setting}); further deliveries to it fail in settlement " +
                        "until its handler catches up. Publishers and every other binding are unaffected.",
                        Declaration.Id,
                        waiting,
                        budget,
                        Constants.Configuration.InProcessBacklogBytes);
                    break;
                case Admission.Overflow:
                    publication.Operation.MarkOverflowed();
                    break;
                default:
                    publication.Operation.MarkFailed();
                    break;
            }
        }

        public bool TryTake([MaybeNullWhen(false)] out LocalDelivery delivery)
        {
            if (!_queue.Reader.TryRead(out delivery)) return false;
            Interlocked.Add(ref _waitingBytes, -delivery.Payload.Length);
            return true;
        }

        public void Close()
        {
            lock (_admission)
            {
                _closed = true;
                _queue.Writer.TryComplete();
            }
        }

        private enum Admission
        {
            Accepted,
            OverflowBegins,
            Overflow,
            Closed
        }
    }
}
