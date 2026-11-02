using Koan.Communication.Adapters;
using Koan.Communication.Semantics;
using Koan.Core.Json;
using Koan.Data.Abstractions;
using Koan.Data.Core;

namespace Koan.Communication.Runtime;

internal sealed class TransportCoordinator(
    CommunicationContextPlan contextPlan,
    CommunicationRouter router)
{
    public async Task<TransportAcceptance> Send<TEntity>(
        IAsyncEnumerable<TEntity> source,
        string? channel,
        CancellationToken ct)
        where TEntity : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(source);

        var route = router.For(CommunicationLane.Transport, channel);
        var contract = CommunicationContractIdentity.Transport(typeof(TEntity));
        var operation = new CommunicationOperation(route);
        if (router.KnownTargetGroups(route, contract) == 0)
        {
            operation.Seal(false);
            throw NoReceivers<TEntity>(operation);
        }

        var capturedContext = contextPlan.Capture(typeof(TEntity), CommunicationLane.Transport);
        var ordinal = 0L;
        try
        {
            // A write in a scope has not landed until the scope commits, and a rollback means it never did.
            if (EntityContext.CurrentTransaction is { IsCompleted: false } scope)
            {
                var copies = await operation.AwaitCommit(source, ct).ConfigureAwait(false);
                scope.AfterCommit(token => Send(CommunicationOperation.Copies<TEntity>(copies), channel, token));
                return new TransportAcceptance(operation.Snapshot());
            }

            await foreach (var entity in source.WithCancellation(ct).ConfigureAwait(false))
            {
                operation.MarkEnumerated();
                string entityPayload;
                try
                {
                    entityPayload = entity.ToJson();
                }
                catch (Exception error)
                {
                    operation.MarkRejected();
                    operation.Seal(false);
                    throw new TransportException(
                        TransportException.FailureKind.Serialization,
                        $"Entity Transport could not serialize '{typeof(TEntity).Name}' at ordinal {ordinal}. " +
                        "Use a finite JSON snapshot without reference cycles or unsupported values.",
                        new TransportAcceptance(operation.Snapshot()),
                        error);
                }

                var publication = CommunicationWireCodec.Encode(new CommunicationWireEnvelope(
                    CommunicationWireCodec.SchemaVersion,
                    router.MeshId,
                    CommunicationLane.Transport,
                    route.Channel,
                    contract,
                    operation.OperationId,
                    ordinal,
                    entityPayload,
                    capturedContext));
                if (route.MaxPayloadBytes is { } limit && publication.Length > limit)
                {
                    operation.MarkRejected();
                    operation.Seal(false);
                    throw new TransportException(
                        TransportException.FailureKind.PayloadTooLarge,
                        $"Entity Transport rejected '{typeof(TEntity).Name}' at ordinal {ordinal} because its " +
                        $"{publication.Length}-byte publication exceeds the {limit}-byte transport limit that provider " +
                        $"'{route.AdapterId}' declares. Send a smaller snapshot, or elect a provider whose transport " +
                        "carries it.",
                        new TransportAcceptance(operation.Snapshot()));
                }

                try
                {
                    var accepted = await router.Publish(
                            route,
                            contract,
                            MessageId(operation.OperationId, ordinal),
                            publication,
                            operation,
                            ct)
                        .ConfigureAwait(false);
                    operation.ReserveAcceptance(accepted.TargetGroups, accepted.SettlementObservable);
                }
                catch (OperationCanceledException error) when (ct.IsCancellationRequested)
                {
                    operation.MarkRejected();
                    operation.Seal(false);
                    throw Canceled(operation, error, ct);
                }
                catch (CommunicationAdapterException error)
                {
                    operation.MarkRejected();
                    operation.Seal(false);
                    if (error.Failure == CommunicationAdapterException.FailureKind.NoRoute)
                        throw NoReceivers<TEntity>(operation, error);
                    throw ProviderUnavailable(operation, route, error);
                }
                catch (Exception error)
                {
                    operation.MarkRejected();
                    operation.Seal(false);
                    throw ProviderUnavailable(operation, route, error);
                }

                ordinal++;
            }

            operation.Seal(true);
            return new TransportAcceptance(operation.Snapshot());
        }
        catch (TransportException) { throw; }
        catch (TransportCanceledException) { throw; }
        catch (OperationCanceledException error) when (ct.IsCancellationRequested)
        {
            operation.Seal(false);
            throw Canceled(operation, error, ct);
        }
        catch (Exception error)
        {
            operation.Seal(false);
            throw new TransportException(
                TransportException.FailureKind.SourceFailed,
                $"Entity Transport source '{typeof(TEntity).Name}' failed after {ordinal} accepted snapshot(s).",
                new TransportAcceptance(operation.Snapshot()),
                error);
        }
    }

    private static string MessageId(Guid operationId, long ordinal) => $"{operationId:N}:{ordinal}";

    private static TransportException NoReceivers<TEntity>(
        CommunicationOperation operation,
        Exception? error = null)
        => new(
            TransportException.FailureKind.NoReceivers,
            $"Entity Transport has no route to an IReceiveEntity<{typeof(TEntity).Name}> receiver group. " +
            "Start a receiver on the elected mesh, add a business-named local receiver, or remove this Send call.",
            new TransportAcceptance(operation.Snapshot()),
            error);

    private static TransportException ProviderUnavailable(
        CommunicationOperation operation,
        CommunicationRouteDecision route,
        Exception error)
        => new(
            TransportException.FailureKind.ProviderUnavailable,
            $"Entity Transport provider '{route.AdapterId}' could not accept the snapshot. " +
            "Inspect Communication readiness and provider facts; Koan will not fall back to process-local reach.",
            new TransportAcceptance(operation.Snapshot()),
            error);

    private static TransportCanceledException Canceled(
        CommunicationOperation operation,
        OperationCanceledException error,
        CancellationToken ct)
        => new(
            "Entity Transport publication was canceled; the acceptance reports the already accepted prefix.",
            new TransportAcceptance(operation.Snapshot()),
            error,
            ct);
}
