using System.Security.Cryptography;
using System.Text;
using Koan.Communication.Adapters;
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

[ProviderPriority(100)]
internal sealed class RabbitMqCommunicationAdapter(
    IOptions<RabbitMqCommunicationOptions> options,
    IServiceDiscoveryCoordinator discovery,
    ILogger<RabbitMqCommunicationAdapter> logger)
    : ICommunicationAdapter
{
    private static readonly CommunicationAdapterDescriptor AdapterDescriptor = new(
        Constants.ProviderId,
        [
            CommunicationLane.Transport,
            CommunicationLane.FrameworkSignals,
            CommunicationLane.FrameworkBroadcasts
        ],
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
        IngressTrust: ContextIngressTrust.Authenticated);

    private readonly SemaphoreSlim _publisherGate = new(1, 1);
    private IConnection? _connection;
    private IChannel? _publisher;
    private IChannel? _consumer;
    private byte[]? _trustKey;
    private string? _exchange;
    private string? _userId;
    private int _state;

    public CommunicationAdapterDescriptor Descriptor => AdapterDescriptor;

    public bool IsReady => Volatile.Read(ref _state) == 1
                           && _connection?.IsOpen == true
                           && _publisher?.IsOpen == true
                           && _consumer?.IsOpen == true;

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
                ClientProvidedName = $"koan.communication.{Topology.Slug(host.MeshId)}",
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5),
                ConsumerDispatchConcurrency = 1
            };
            _userId = factory.UserName;
            _trustKey = DeriveTrustKey(options.Value.MeshTrustKey, factory.Password, host.MeshId);
            _exchange = Topology.Exchange(host.MeshId);

            _connection = await factory.CreateConnectionAsync(ct).ConfigureAwait(false);
            _publisher = await _connection.CreateChannelAsync(
                    new CreateChannelOptions(
                        publisherConfirmationsEnabled: true,
                        publisherConfirmationTrackingEnabled: true,
                        outstandingPublisherConfirmationsRateLimiter: null,
                        consumerDispatchConcurrency: null),
                    ct)
                .ConfigureAwait(false);
            _consumer = await _connection.CreateChannelAsync(
                    new CreateChannelOptions(
                        publisherConfirmationsEnabled: false,
                        publisherConfirmationTrackingEnabled: false,
                        outstandingPublisherConfirmationsRateLimiter: null,
                        consumerDispatchConcurrency: 1),
                    ct)
                .ConfigureAwait(false);

            await _publisher.ExchangeDeclareAsync(
                    _exchange,
                    ExchangeType.Direct,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: ct)
                .ConfigureAwait(false);
            await _consumer.ExchangeDeclareAsync(
                    _exchange,
                    ExchangeType.Direct,
                    durable: true,
                    autoDelete: false,
                    cancellationToken: ct)
                .ConfigureAwait(false);
            await _consumer.BasicQosAsync(0, options.Value.Prefetch, global: false, ct).ConfigureAwait(false);

            foreach (var binding in host.Bindings
                         .Where(binding => AdapterDescriptor.Lanes.Contains(binding.Lane))
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
                "or broker readiness; Koan will not silently reduce the elected mesh to process-local reach.");
        }
    }

    public async ValueTask<CommunicationAdapterAcceptance> Publish(
        CommunicationAdapterPublication publication,
        CancellationToken ct)
    {
        if (!AdapterDescriptor.Lanes.Contains(publication.Lane))
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.Unavailable,
                $"The RabbitMQ provider does not claim {publication.Lane}.");
        EnsureReady();

        await _publisherGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            EnsureReady();
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
                await _publisher!.BasicPublishAsync(
                        _exchange!,
                        Topology.Route(publication.Lane, publication.Channel, publication.ContractId),
                        mandatory: true,
                        properties,
                        body,
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
            catch (CommunicationAdapterException)
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
        finally
        {
            _publisherGate.Release();
        }
    }

    public async Task Stop(CancellationToken ct)
    {
        if (Interlocked.Exchange(ref _state, 2) != 1) return;
        await DisposeBrokerObjects(ct).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _state, 2);
        await DisposeBrokerObjects().ConfigureAwait(false);
        _publisherGate.Dispose();
    }

    private async Task Bind(
        CommunicationAdapterHost host,
        CommunicationAdapterBinding binding,
        CancellationToken ct)
    {
        var queue = Topology.Queue(host.MeshId, binding);
        var nodeScoped = binding.Scope == CommunicationBindingScope.Node;
        await _consumer!.QueueDeclareAsync(
                queue,
                durable: !nodeScoped,
                exclusive: false,
                autoDelete: nodeScoped,
                cancellationToken: ct)
            .ConfigureAwait(false);
        await _consumer.QueueBindAsync(
                queue,
                _exchange!,
                Topology.Route(binding.Lane, binding.Channel, binding.ContractId),
                cancellationToken: ct)
            .ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(_consumer);
        consumer.ReceivedAsync += async (_, delivery) =>
        {
            var body = delivery.Body.ToArray();
            try
            {
                if (!ValidSignature(delivery.BasicProperties.Headers, body))
                {
                    logger.LogError(
                        "RabbitMQ Communication rejected an unauthenticated envelope for binding {BindingId}.",
                        binding.Id);
                    await _consumer.BasicRejectAsync(delivery.DeliveryTag, requeue: false).ConfigureAwait(false);
                    return;
                }

                var outcome = await host.Dispatch(
                        binding.Id,
                        body)
                    .ConfigureAwait(false);
                if (outcome is CommunicationDeliveryOutcome.Delivered or CommunicationDeliveryOutcome.Filtered)
                    await _consumer.BasicAckAsync(delivery.DeliveryTag, multiple: false).ConfigureAwait(false);
                else
                    await _consumer.BasicRejectAsync(delivery.DeliveryTag, requeue: false).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                logger.LogError(
                    "RabbitMQ Communication receiver group {BindingId} failed: {Error}",
                    binding.Id,
                    Koan.Core.Redaction.DeIdentify(error.Message));
                try { await _consumer.BasicRejectAsync(delivery.DeliveryTag, requeue: false).ConfigureAwait(false); }
                catch { /* channel recovery and health own the secondary failure */ }
            }
        };
        await _consumer.BasicConsumeAsync(queue, autoAck: false, consumer, ct).ConfigureAwait(false);
    }

    private async Task<string> ResolveEndpoint(CancellationToken ct)
    {
        var configured = options.Value.ConnectionString?.Trim();
        if (!string.IsNullOrWhiteSpace(configured)
            && !string.Equals(configured, "auto", StringComparison.OrdinalIgnoreCase))
            return configured;

        var result = await discovery.DiscoverService(
                Constants.Broker.ServiceName,
                new DiscoveryContext
                {
                    OrchestrationMode = KoanEnv.OrchestrationMode,
                    RequireHealthValidation = true,
                    HealthCheckTimeout = TimeSpan.FromSeconds(5)
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
        return hmac.ComputeHash(Encoding.UTF8.GetBytes($"koan.communication.rabbitmq.v1:{meshId}"));
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
        if (!IsReady)
            throw new CommunicationAdapterException(
                CommunicationAdapterException.FailureKind.Unavailable,
                "RabbitMQ Communication is not ready for confirmed publication.");
    }

    private async Task DisposeBrokerObjects(CancellationToken ct = default)
    {
        var consumer = Interlocked.Exchange(ref _consumer, null);
        var publisher = Interlocked.Exchange(ref _publisher, null);
        var connection = Interlocked.Exchange(ref _connection, null);
        if (consumer is not null) await Close(consumer, ct).ConfigureAwait(false);
        if (publisher is not null) await Close(publisher, ct).ConfigureAwait(false);
        if (connection is not null)
        {
            try { if (connection.IsOpen) await connection.CloseAsync(ct).ConfigureAwait(false); }
            catch { }
            await connection.DisposeAsync().ConfigureAwait(false);
        }
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
            => $"{Constants.Broker.ExchangePrefix}.{Slug(meshId)}.v3";

        internal static string Queue(string meshId, CommunicationAdapterBinding binding)
            => $"{Exchange(meshId)}.{Lane(binding.Lane)}.channel.{Hash(binding.Channel)}.group." +
               $"{Hash(binding.GroupId)}.{Hash(binding.ContractId)}";

        internal static string Route(CommunicationLane lane, string channel, string contractId)
            => $"{Lane(lane)}.channel.{Hash(channel)}.contract.{Hash(contractId)}";

        internal static string MessageType(CommunicationLane lane)
            => $"{Constants.Broker.MessageTypePrefix}.{Lane(lane)}.v2";

        private static string Lane(CommunicationLane lane) => lane.ToString().ToLowerInvariant();

        internal static string Slug(string value)
        {
            var chars = value.ToLowerInvariant()
                .Select(static ch => char.IsLetterOrDigit(ch) ? ch : '-')
                .ToArray();
            var normalized = new string(chars).Trim('-');
            return string.IsNullOrWhiteSpace(normalized) ? "koan-app" : normalized;
        }

        private static string Hash(string value)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))[..24].ToLowerInvariant();
    }
}
