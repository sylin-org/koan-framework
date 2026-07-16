using System.Text;
using Koan.Core.Context;
using Koan.Core.Json;
using Koan.Data.Abstractions;
using Microsoft.Extensions.Options;

namespace Koan.Communication.Runtime;

internal sealed class TransportCoordinator(
    CommunicationHandlerCatalog handlers,
    KoanContextCarrierRegistry contextCarriers,
    InProcessCommunicationRuntime runtime,
    IOptions<CommunicationOptions> options)
{
    public async Task<TransportAcceptance> Send<TEntity>(
        IAsyncEnumerable<TEntity> source,
        CancellationToken ct)
        where TEntity : class, IEntity
    {
        ArgumentNullException.ThrowIfNull(source);

        var targets = handlers.TransportFor(typeof(TEntity));
        var operation = new CommunicationOperation(targets.Count);
        var capturedContext = contextCarriers.Capture();
        if (targets.Count == 0)
        {
            operation.Seal(false);
            throw new TransportException(
                TransportException.FailureKind.NoReceivers,
                $"Entity Transport has no IReceiveEntity<{typeof(TEntity).Name}> receiver group. " +
                "Add a business-named receiver class or remove this Send call.",
                new TransportAcceptance(operation.Snapshot()));
        }

        try
        {
            runtime.EnsureAvailable();
        }
        catch (Exception ex)
        {
            operation.Seal(false);
            throw ProviderUnavailable(operation, ex);
        }

        var ordinal = 0L;
        try
        {
            await foreach (var entity in source.WithCancellation(ct).ConfigureAwait(false))
            {
                operation.MarkEnumerated();
                string payload;
                try
                {
                    payload = entity.ToJson();
                }
                catch (Exception ex)
                {
                    operation.MarkRejected();
                    operation.Seal(false);
                    throw new TransportException(
                        TransportException.FailureKind.Serialization,
                        $"Entity Transport could not serialize '{typeof(TEntity).Name}' at ordinal {ordinal}. " +
                        "Use a finite JSON snapshot without reference cycles or unsupported values.",
                        new TransportAcceptance(operation.Snapshot()),
                        ex);
                }

                if (Encoding.UTF8.GetByteCount(payload) > options.Value.MaxPayloadBytes)
                {
                    operation.MarkRejected();
                    operation.Seal(false);
                    throw new TransportException(
                        TransportException.FailureKind.PayloadTooLarge,
                        $"Entity Transport rejected '{typeof(TEntity).Name}' at ordinal {ordinal} because its " +
                        $"snapshot exceeds the configured {nameof(CommunicationOptions.MaxPayloadBytes)} limit.",
                        new TransportAcceptance(operation.Snapshot()));
                }

                var envelope = new TransportEnvelope(
                    operation,
                    ordinal,
                    typeof(TEntity),
                    payload,
                    capturedContext,
                    targets.Cast<CommunicationTargetBinding>().ToArray());
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
            return new TransportAcceptance(operation.Snapshot());
        }
        catch (TransportException)
        {
            throw;
        }
        catch (TransportCanceledException)
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
            throw new TransportException(
                TransportException.FailureKind.SourceFailed,
                $"Entity Transport source '{typeof(TEntity).Name}' failed after {ordinal} accepted snapshot(s).",
                new TransportAcceptance(operation.Snapshot()),
                ex);
        }
    }

    private static TransportException ProviderUnavailable(CommunicationOperation operation, Exception error)
        => new(
            TransportException.FailureKind.ProviderUnavailable,
            "Entity Transport could not accept the snapshot into the process-local channel. " +
            "Ensure the Koan host is running and not stopping.",
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
