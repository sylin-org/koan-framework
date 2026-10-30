using System.Security.Cryptography;
using System.Text;
using Koan.Communication.Adapters;
using Koan.Communication.Connector.RabbitMq.Publishing;
using Koan.Core;
using Koan.Core.Context;
using Koan.Core.Orchestration;
using Koan.Core.Orchestration.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using Constants = Koan.Communication.Connector.RabbitMq.Infrastructure.Constants;

namespace Koan.Communication.Connector.RabbitMq;

/// <summary>
/// RabbitMQ carriage for Transport and the framework routes. Publications share one confirm-tracking channel, and up
/// to <see cref="Constants.Broker.OutstandingConfirms"/> of them await their confirms together rather than one round
/// trip at a time; the adapter's <see cref="PublisherConfirmWindow"/> holds that bound across each awaited confirm. Every receiver group consumes on its own
/// channel, so a slow or hung group never holds another. A durable group's failed delivery is redelivered a bounded
/// number of times and then dead-lettered to that group's own dead-letter queue.
/// </summary>
[ProviderPriority(100)]
internal sealed class RabbitMqCommunicationAdapter(
    IOptions<RabbitMqCommunicationOptions> options,
    IServiceDiscoveryCoordinator discovery,
    ILogger<RabbitMqCommunicationAdapter> logger)
    : ICommunicationAdapter
{
    private static readonly CommunicationLane[] ClaimedLanes =
    [
        CommunicationLane.Transport,
        CommunicationLane.FrameworkSignals,
        CommunicationLane.FrameworkBroadcasts
    ];

    private readonly CancellationTokenSource _abort = new();
    private IConnection? _connection;
    private IChannel? _publisher;
    private PublisherConfirmWindow? _confirms;
    private IChannel[] _consumers = [];
    private byte[]? _trustKey;
    private string? _exchange;
    private string? _deadLetterExchange;
    private string? _userId;
    private int _state;

    private CommunicationAdapterDescriptor? _descriptor;

    public CommunicationAdapterDescriptor Descriptor => _descriptor ??= new(
        Constants.ProviderId,
        ClaimedLanes,
        CommunicationDeliveryAssurance.DurablyAcknowledged,
        CommunicationAdapterCapabilities.ContractIdentity
        | CommunicationAdapterCapabilities.SnapshotCopy
        | CommunicationAdapterCapabilities.ContextCarriage
        | CommunicationAdapterCapabilities.TypedGroups
        | CommunicationAdapterCapabilities.GroupFanOut
        | CommunicationAdapterCapabilities.NodeFanOut
        | CommunicationAdapterCapabilities.MessageIdentity
        | CommunicationAdapterCapabilities.BoundedAcceptance,
        [Constants.ProjectReference, Constants.PackageReference],
        IngressTrust: ContextIngressTrust.Authenticated,
        MaxPayloadBytes: options.Value.MaxMessageBytes);

    public bool IsReady => Volatile.Read(ref _state) == 1
                           && _connection?.IsOpen == true
                           && _publisher?.IsOpen == true
                           && Volatile.Read(ref _consumers).All(static channel => channel.IsOpen);

    internal string? LastFailure { get; private set; }
    internal bool IsActivated => Volatile.Read(ref _state) != 0;

    public async Task Start(CommunicationAdapterHost host, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            throw new InvalidOperationException("The RabbitMQ Communication provider cannot be started more than once.");

        try
        {
            var endpoint = await ResolveEndpoint(ct).ConfigureAwait(false);
            var factory = new ConnectionFactory
            {
                Uri = new Uri(endpoint),
                ClientProvidedName = $"{Constants.Broker.ClientNamePrefix}.{Topology.Slug(host.MeshId)}",
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = Constants.Broker.NetworkRecoveryInterval,
                ConsumerDispatchConcurrency = 1
            };
            _userId = factory.UserName;
            _trustKey = DeriveTrustKey(options.Value.MeshTrustKey, factory.Password, host.MeshId);
            _exchange = Topology.Exchange(host.MeshId);
            _deadLetterExchange = Topology.DeadLetterExchange(host.MeshId);

            _connection = await factory.CreateConnectionAsync(ct).ConfigureAwait(false);
            _confirms = new PublisherConfirmWindow(Constants.Broker.OutstandingConfirms);
            // The client's own outstanding-confirmation limiter is off: it releases its lease once the frame is
            // written, before the confirm, so it cannot bound outstanding confirms. The confirm window does.
            _publisher = await _connection.CreateChannelAsync(
                    new CreateChannelOptions(
                        publisherConfirmationsEnabled: true,
                        publisherConfirmationTrackingEnabled: true,
                        outstandingPublisherConfirmationsRateLimiter: null,
                        consumerDispatchConcurrency: null),
                    ct)
                .ConfigureAwait(false);

            await _publisher.ExchangeDeclareAsync(
                    _exchange,
                    ExchangeType.Direct,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: ct)
                .ConfigureAwait(false);
            await _publisher.ExchangeDeclareAsync(
                    _deadLetterExchange,
                    ExchangeType.Direct,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: ct)
                .ConfigureAwait(false);

            foreach (var binding in host.Bindings
                         .Where(static binding => ClaimedLanes.Contains(binding.Lane))
                         .OrderBy(static binding => binding.Id, StringComparer.Ordinal))
            {
                await Bind(host, binding, ct).ConfigureAwait(false);
            }

            LastFailure = null;
        }
        catch (Exception error)
        {
            LastFailure = Koan.Core.Redaction.DeIdentify(error.Message);
            Interlocked.Exchange(ref _state, 2);
            await DisposeBrokerObjects().ConfigureAwait(false);
            logger.LogError(
                "RabbitMQ Communication startup failed: {Error}",
                LastFailure);
            throw new InvalidOperationException(
                "RabbitMQ Communication is directly selected but unavailable. Correct the endpoint, credentials, " +
                "or broker readiness; Koan will not silently reduce the elected mesh to process-local reach. " +
                $"Broker reported: {LastFailure}");
        }
    }

    public async ValueTask<CommunicationAdapterAcceptance> Publish(
        CommunicationAdapterPublication publication,
        CancellationToken ct)
    {
        if (!ClaimedLanes.Contains(publication.Lane))
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.Unavailable,
                $"The RabbitMQ provider does not claim {publication.Lane}.");
        EnsureReady();
        var publisher = _publisher ?? throw NotReady();
        var confirms = _confirms ?? throw NotReady();

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Value.PublishTimeout);
        var body = publication.Payload;
        var properties = new BasicProperties
        {
            AppId = _exchange,
            ContentType = Constants.Broker.ContentType,
            DeliveryMode = DeliveryModes.Persistent,
            MessageId = publication.MessageId,
            Type = Topology.MessageType(publication.Lane),
            UserId = _userId,
            Headers = new Dictionary<string, object?>
            {
                [Constants.Broker.SignatureHeader] = Sign(body.Span)
            }
        };

        try
        {
            await confirms.Publish(
                    () => publisher.BasicPublishAsync(
                        _exchange!,
                        Topology.Route(publication.Lane, publication.Channel, publication.ContractId),
                        mandatory: true,
                        properties,
                        body,
                        timeout.Token),
                    timeout.Token)
                .ConfigureAwait(false);
        }
        catch (PublishException error) when (error.IsReturn)
        {
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.NoRoute,
                $"RabbitMQ has no bound receiver group for contract '{publication.ContractId}'.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.Unavailable,
                "RabbitMQ did not confirm publication before the configured timeout.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            LastFailure = Koan.Core.Redaction.DeIdentify(error.Message);
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.Unavailable,
                "RabbitMQ could not confirm publication.");
        }

        return new CommunicationAdapterAcceptance(TargetGroups: null, SettlementObservable: false);
    }

    public async Task Stop(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _state, 2) != 1) return;
        _abort.Cancel();
        await DisposeBrokerObjects(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _state, 2);
        _abort.Cancel();
        await DisposeBrokerObjects().ConfigureAwait(false);
        _abort.Dispose();
    }

    /// <summary>
    /// Declares the group's queue and consumes it on a channel of its own. A durable group's queue is a quorum queue
    /// that counts failed deliveries and dead-letters past <see cref="Constants.Broker.DeliveryLimit"/>; a node
    /// broadcast's queue is ephemeral and its failures are dropped, because its owning module keeps a correctness
    /// fallback.
    /// </summary>
    private async Task Bind(
        CommunicationAdapterHost host,
        CommunicationAdapterBinding binding,
        CancellationToken ct)
    {
        var channel = await _connection!.CreateChannelAsync(
                new CreateChannelOptions(
                    publisherConfirmationsEnabled: false,
                    publisherConfirmationTrackingEnabled: false,
                    outstandingPublisherConfirmationsRateLimiter: null,
                    consumerDispatchConcurrency: 1),
                ct)
            .ConfigureAwait(false);
        Volatile.Write(ref _consumers, [.. Volatile.Read(ref _consumers), channel]);
        await channel.BasicQosAsync(0, options.Value.Prefetch, global: false, ct).ConfigureAwait(false);

        var queue = Topology.Queue(host.MeshId, binding);
        var nodeScoped = binding.Scope == CommunicationBindingScope.Node;
        if (nodeScoped)
        {
            await channel.QueueDeclareAsync(
                    queue,
                    durable: false,
                    exclusive: true,
                    autoDelete: true,
                    cancellationToken: ct)
                .ConfigureAwait(false);
        }
        else
        {
            var deadLetters = Topology.DeadLetterQueue(host.MeshId, binding);
            await channel.QueueDeclareAsync(
                    deadLetters,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: Topology.DeadLetterQueueArguments(),
                    cancellationToken: ct)
                .ConfigureAwait(false);
            await channel.QueueBindAsync(deadLetters, _deadLetterExchange!, queue, cancellationToken: ct)
                .ConfigureAwait(false);
            await channel.QueueDeclareAsync(
                    queue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false,
                    arguments: Topology.GroupQueueArguments(_deadLetterExchange!, queue),
                    cancellationToken: ct)
                .ConfigureAwait(false);
        }

        await channel.QueueBindAsync(
                queue,
                _exchange!,
                Topology.Route(binding.Lane, binding.Channel, binding.ContractId),
                cancellationToken: ct)
            .ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, delivery) => Receive(host, binding, channel, delivery, nodeScoped);
        await channel.BasicConsumeAsync(queue, autoAck: false, consumer, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Settles one delivery with the broker. A completed or filtered delivery is acknowledged. A failed one is
    /// rejected with requeue, which the quorum queue counts toward its delivery limit; a delivery interrupted by host
    /// stop is returned without being counted. An envelope that fails authentication is dead-lettered at once.
    /// </summary>
    private async Task Receive(
        CommunicationAdapterHost host,
        CommunicationAdapterBinding binding,
        IChannel channel,
        BasicDeliverEventArgs delivery,
        bool nodeScoped)
    {
        var body = delivery.Body.ToArray();
        try
        {
            if (!ValidSignature(delivery.BasicProperties.Headers, body))
            {
                logger.LogError(
                    "RabbitMQ Communication rejected an unauthenticated envelope for binding {BindingId}.",
                    binding.Id);
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: false).ConfigureAwait(false);
                return;
            }

            var outcome = await host.Dispatch(binding.Id, body, _abort.Token).ConfigureAwait(false);
            if (outcome is CommunicationDeliveryOutcome.Delivered or CommunicationDeliveryOutcome.Filtered)
                await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false).ConfigureAwait(false);
            else if (_abort.IsCancellationRequested)
                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true).ConfigureAwait(false);
            else
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: !nodeScoped).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            logger.LogError(
                "RabbitMQ Communication receiver group {BindingId} failed: {Error}",
                binding.Id,
                Koan.Core.Redaction.DeIdentify(error.Message));
            try
            {
                await channel.BasicRejectAsync(delivery.DeliveryTag, requeue: !nodeScoped).ConfigureAwait(false);
            }
            catch
            {
                // Channel recovery and health own the secondary failure.
            }
        }
    }

    private async Task<string> ResolveEndpoint(CancellationToken ct)
    {
        var result = await discovery.DiscoverService(
                Constants.Broker.ServiceName,
                new DiscoveryContext
                {
                    OrchestrationMode = KoanEnv.OrchestrationMode,
                    RequireHealthValidation = true,
                    HealthCheckTimeout = Constants.Broker.DiscoveryHealthTimeout
                },
                ct)
            .ConfigureAwait(false);
        if (!result.IsSuccessful || string.IsNullOrWhiteSpace(result.ServiceUrl))
            throw new InvalidOperationException("RabbitMQ service discovery found no reachable endpoint.");
        return result.ServiceUrl;
    }

    private static byte[] DeriveTrustKey(string? explicitKey, string? brokerPassword, string meshId)
    {
        var material = string.IsNullOrWhiteSpace(explicitKey) ? brokerPassword : explicitKey;
        if (string.IsNullOrWhiteSpace(material))
            throw new InvalidOperationException(
                "RabbitMQ cannot authenticate Koan context without MeshTrustKey or an authenticated broker credential.");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(material));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes($"{Constants.Broker.TrustDerivationPrefix}:{meshId}"));
    }

    private byte[] Sign(ReadOnlySpan<byte> body)
    {
        using var hmac = new HMACSHA256(_trustKey!);
        return hmac.ComputeHash(body.ToArray());
    }

    private bool ValidSignature(IDictionary<string, object?>? headers, ReadOnlySpan<byte> body)
    {
        if (headers is null
            || !headers.TryGetValue(Constants.Broker.SignatureHeader, out var raw)
            || raw is not byte[] signature)
            return false;
        var expected = Sign(body);
        return signature.Length == expected.Length
               && CryptographicOperations.FixedTimeEquals(signature, expected);
    }

    private void EnsureReady()
    {
        if (!IsReady) throw NotReady();
    }

    private static CommunicationAdapterException NotReady()
        => new(
            CommunicationAdapterException.FailureKind.Unavailable,
            "RabbitMQ Communication is not ready for confirmed publication.");

    private async Task DisposeBrokerObjects(CancellationToken ct = default)
    {
        var consumers = Interlocked.Exchange(ref _consumers, Array.Empty<IChannel>());
        var publisher = Interlocked.Exchange(ref _publisher, null);
        var connection = Interlocked.Exchange(ref _connection, null);
        var confirms = Interlocked.Exchange(ref _confirms, null);
        foreach (var consumer in consumers) await Close(consumer, ct).ConfigureAwait(false);
        if (publisher is not null) await Close(publisher, ct).ConfigureAwait(false);
        if (connection is not null)
        {
            try { if (connection.IsOpen) await connection.CloseAsync(ct).ConfigureAwait(false); }
            catch { }
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        if (confirms is not null) await confirms.DisposeAsync().ConfigureAwait(false);
    }

    private static async Task Close(IChannel channel, CancellationToken ct)
    {
        try { if (channel.IsOpen) await channel.CloseAsync(ct).ConfigureAwait(false); }
        catch { }
        await channel.DisposeAsync().ConfigureAwait(false);
    }

    internal static class Topology
    {
        internal static string Exchange(string meshId)
            => $"{Constants.Broker.ExchangePrefix}.{Slug(meshId)}.{Constants.Broker.ExchangeGeneration}";

        internal static string DeadLetterExchange(string meshId)
            => $"{Exchange(meshId)}.{Constants.Broker.DeadLetterSuffix}";

        internal static string Queue(string meshId, CommunicationAdapterBinding binding)
            => $"{Exchange(meshId)}.{Lane(binding.Lane)}.channel.{Hash(binding.Channel)}.group." +
               $"{Hash(binding.GroupId)}.{Hash(binding.ContractId)}";

        /// <summary>The receiver group's own dead-letter queue, bound to the mesh's dead-letter exchange by the group queue's name.</summary>
        internal static string DeadLetterQueue(string meshId, CommunicationAdapterBinding binding)
            => $"{Queue(meshId, binding)}.{Constants.Broker.DeadLetterSuffix}";

        internal static Dictionary<string, object?> GroupQueueArguments(string deadLetterExchange, string queue)
            => new(StringComparer.Ordinal)
            {
                [Constants.Broker.QueueArguments.Type] = Constants.Broker.QueueArguments.Quorum,
                [Constants.Broker.QueueArguments.DeliveryLimit] = Constants.Broker.DeliveryLimit,
                [Constants.Broker.QueueArguments.DeadLetterExchange] = deadLetterExchange,
                [Constants.Broker.QueueArguments.DeadLetterRoutingKey] = queue,
                [Constants.Broker.QueueArguments.DeadLetterStrategy] = Constants.Broker.QueueArguments.AtLeastOnce,
                [Constants.Broker.QueueArguments.Overflow] = Constants.Broker.QueueArguments.RejectPublish
            };

        internal static Dictionary<string, object?> DeadLetterQueueArguments()
            => new(StringComparer.Ordinal)
            {
                [Constants.Broker.QueueArguments.Type] = Constants.Broker.QueueArguments.Quorum
            };

        internal static string Route(CommunicationLane lane, string channel, string contractId)
            => $"{Lane(lane)}.channel.{Hash(channel)}.contract.{Hash(contractId)}";

        internal static string MessageType(CommunicationLane lane)
            => $"{Constants.Broker.MessageTypePrefix}.{Lane(lane)}.{Constants.Broker.MessageGeneration}";

        private static string Lane(CommunicationLane lane) => lane.ToString().ToLowerInvariant();

        internal static string Slug(string value)
        {
            var chars = value.ToLowerInvariant()
                .Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-')
                .ToArray();
            var normalized = new string(chars).Trim('-');
            return string.IsNullOrWhiteSpace(normalized) ? Constants.Broker.DefaultApplicationSlug : normalized;
        }

        private static string Hash(string value)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();
    }
}
