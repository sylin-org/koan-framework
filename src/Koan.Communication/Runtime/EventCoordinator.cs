using System.Text;
using Koan.Communication.Adapters;
using Koan.Communication.Semantics;
using Koan.Core.Context;
using Koan.Core.Json;
using Koan.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Communication.Runtime;

internal sealed class EventCoordinator(
    CommunicationContextPlan contextPlan,
    CommunicationRouter router,
    IOptions<CommunicationOptions> options)
{
    public async Task<EventAcceptance> Raise<TEntity, TEvent>(
        IAsyncEnumerable<TEntity> source,
        TEvent? details,
        bool hasDetails,
        string? channel,
        CancellationToken ct)
        where TEntity : class, IEntity
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var route = router.For(CommunicationLane.Events, channel);
        var contract = CommunicationContractIdentity.Events(typeof(TEntity), typeof(TEvent));
        var operation = new CommunicationOperation(route);
        if (!hasDetails && Attribute.IsDefined(typeof(TEvent), typeof(EventDetailsRequiredAttribute), inherit: false))
        {
            operation.Seal(false);
            throw new EventException(
                EventException.FailureKind.DetailsRequired,
                $"Entity Event '{typeof(TEvent).Name}' requires explicit details. " +
                $"Call Events.Raise(new {typeof(TEvent).Name}(...), ct).",
                new EventAcceptance(operation.Snapshot()));
        }

        string? detailsPayload = null;
        var detailsBytes = 0;
        if (hasDetails)
        {
            try
            {
                detailsPayload = details!.ToJson();
                detailsBytes = Encoding.UTF8.GetByteCount(detailsPayload);
            }
            catch (Exception error)
            {
                operation.Seal(false);
                throw new EventException(
                    EventException.FailureKind.Serialization,
                    $"Entity Event could not serialize details for '{typeof(TEvent).Name}'. " +
                    "Use finite JSON details without reference cycles or unsupported values.",
                    new EventAcceptance(operation.Snapshot()),
                    error);
            }
        }

        var capturedContext = contextPlan.Capture(typeof(TEntity), CommunicationLane.Events);
        var ordinal = 0L;
        try
        {
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
                    throw new EventException(
                        EventException.FailureKind.Serialization,
                        $"Entity Event could not serialize '{typeof(TEntity).Name}' at ordinal {ordinal}. " +
                        "Use a finite JSON snapshot without reference cycles or unsupported values.",
                        new EventAcceptance(operation.Snapshot()),
                        error);
                }

                if (Encoding.UTF8.GetByteCount(entityPayload) + detailsBytes > options.Value.MaxPayloadBytes)
                {
                    operation.MarkRejected();
                    operation.Seal(false);
                    throw new EventException(
                        EventException.FailureKind.PayloadTooLarge,
                        $"Entity Event rejected '{typeof(TEntity).Name}/{typeof(TEvent).Name}' at ordinal {ordinal} " +
                        $"because its serialized snapshot and details exceed the configured " +
                        $"{nameof(CommunicationOptions.MaxPayloadBytes)} limit.",
                        new EventAcceptance(operation.Snapshot()));
                }

                var occurrenceId = Guid.CreateVersion7();
                var wire = new CommunicationWireEnvelope(
                    CommunicationWireCodec.SchemaVersion,
                    router.MeshId,
                    CommunicationLane.Events,
                    route.Channel,
                    contract,
                    operation.OperationId,
                    ordinal,
                    entityPayload,
                    capturedContext,
                    occurrenceId,
                    DateTimeOffset.UtcNow,
                    hasDetails,
                    detailsPayload);
                try
                {
                    var accepted = await router.Publish(
                            route,
                            contract,
                            $"{occurrenceId:N}",
                            CommunicationWireCodec.Encode(wire),
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
            return new EventAcceptance(operation.Snapshot());
        }
        catch (EventException) { throw; }
        catch (EventCanceledException) { throw; }
        catch (OperationCanceledException error) when (ct.IsCancellationRequested)
        {
            operation.Seal(false);
            throw Canceled(operation, error, ct);
        }
        catch (Exception error)
        {
            operation.Seal(false);
            throw new EventException(
                EventException.FailureKind.SourceFailed,
                $"Entity Event source '{typeof(TEntity).Name}' failed after {ordinal} accepted occurrence(s).",
                new EventAcceptance(operation.Snapshot()),
                error);
        }
    }

    private static EventException ProviderUnavailable(
        CommunicationOperation operation,
        CommunicationRouteDecision route,
        Exception error)
        => new(
            EventException.FailureKind.ProviderUnavailable,
            $"Entity Event provider '{route.AdapterId}' could not accept the occurrence. " +
            "Inspect Communication readiness and provider facts; Koan will not fall back to process-local reach.",
            new EventAcceptance(operation.Snapshot()),
            error);

    private static EventCanceledException Canceled(
        CommunicationOperation operation,
        OperationCanceledException error,
        CancellationToken ct)
        => new(
            "Entity Event publication was canceled; the acceptance reports the already accepted prefix.",
            new EventAcceptance(operation.Snapshot()),
            error,
            ct);
}
