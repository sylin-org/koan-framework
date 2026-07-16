using System.Text;
using Koan.Core.Context;
using Koan.Core.Json;
using Koan.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Communication.Runtime;

internal sealed class EventCoordinator(
    CommunicationHandlerCatalog handlers,
    KoanContextCarrierRegistry contextCarriers,
    InProcessCommunicationRuntime runtime,
    IOptions<CommunicationOptions> options)
{
    public async Task<EventAcceptance> Raise<TEntity, TEvent>(
        IAsyncEnumerable<TEntity> source,
        TEvent? details,
        bool hasDetails,
        CancellationToken ct)
        where TEntity : class, IEntity
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(source);

        var targets = handlers.EventsFor(typeof(TEntity), typeof(TEvent));
        var operation = new CommunicationOperation(targets.Count);
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
            catch (Exception ex)
            {
                operation.Seal(false);
                throw new EventException(
                    EventException.FailureKind.Serialization,
                    $"Entity Event could not serialize details for '{typeof(TEvent).Name}'. " +
                    "Use finite JSON details without reference cycles or unsupported values.",
                    new EventAcceptance(operation.Snapshot()),
                    ex);
            }
        }

        var capturedContext = contextCarriers.Capture();
        try
        {
            runtime.EnsureAvailable();
        }
        catch (Exception ex)
        {
            operation.Seal(false);
            throw ProviderUnavailable(operation, ex);
        }

        var targetBindings = targets.Cast<CommunicationTargetBinding>().ToArray();
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
                catch (Exception ex)
                {
                    operation.MarkRejected();
                    operation.Seal(false);
                    throw new EventException(
                        EventException.FailureKind.Serialization,
                        $"Entity Event could not serialize '{typeof(TEntity).Name}' at ordinal {ordinal}. " +
                        "Use a finite JSON snapshot without reference cycles or unsupported values.",
                        new EventAcceptance(operation.Snapshot()),
                        ex);
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

                var envelope = new EventEnvelope(
                    operation,
                    ordinal,
                    typeof(TEntity),
                    entityPayload,
                    capturedContext,
                    targetBindings,
                    typeof(TEvent),
                    Guid.CreateVersion7(),
                    DateTimeOffset.UtcNow,
                    hasDetails,
                    detailsPayload);
                operation.ReserveAcceptance();
                try
                {
                    await runtime.Accept(envelope, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
                {
                    operation.RollBackAcceptance();
                    operation.MarkRejected();
                    operation.Seal(false);
                    throw Canceled(operation, ex, ct);
                }
                catch (Exception ex)
                {
                    operation.RollBackAcceptance();
                    operation.MarkRejected();
                    operation.Seal(false);
                    throw ProviderUnavailable(operation, ex);
                }

                ordinal++;
            }

            operation.Seal(true);
            return new EventAcceptance(operation.Snapshot());
        }
        catch (EventException)
        {
            throw;
        }
        catch (EventCanceledException)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            operation.Seal(false);
            throw Canceled(operation, ex, ct);
        }
        catch (Exception ex)
        {
            operation.Seal(false);
            throw new EventException(
                EventException.FailureKind.SourceFailed,
                $"Entity Event source '{typeof(TEntity).Name}' failed after {ordinal} accepted occurrence(s).",
                new EventAcceptance(operation.Snapshot()),
                ex);
        }
    }

    private static EventException ProviderUnavailable(CommunicationOperation operation, Exception error)
        => new(
            EventException.FailureKind.ProviderUnavailable,
            "Entity Events could not accept the occurrence into the process-local channel. " +
            "Ensure the Koan host is running and not stopping.",
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
