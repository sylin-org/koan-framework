using System;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Cache.Abstractions.Coherence;

/// <summary>
/// Transport-agnostic cross-node coordination channel. One implementation per transport
/// (Redis pub/sub, Redis Streams, Koan.Messaging bus, in-process, ...).
/// </summary>
/// <remarks>
/// <para>
/// The contract is intentionally cache-agnostic — only the payload type
/// (<typeparamref name="TMessage"/>) is concern-specific. This shape is designed to be
/// promoted to <c>Koan.Core.Coherence</c> once a second consumer materializes (cluster
/// events, feature-flag propagation, service-discovery hot-reload). For v1 it lives in
/// <c>Koan.Cache.Abstractions.Coherence</c> to avoid creating a new project before there
/// is demand; the migration is a file move with no type renames.
/// </para>
/// <para>
/// Implementations declare <c>[ProviderPriority(N)]</c> (from <c>Koan.Core</c>)
/// so coordinators can pick a winner when multiple channels are registered. Higher priority
/// wins; ties broken by registration order; explicit config pin overrides.
/// </para>
/// <para>
/// Coordination is best-effort by design. <see cref="Publish"/> failures are logged but
/// must not throw. Consumers requiring durability should layer their own retry/persistence.
/// </para>
/// </remarks>
/// <typeparam name="TMessage">The payload type carried by this channel.</typeparam>
public interface ICoherenceChannel<TMessage> where TMessage : struct
{
    /// <summary>Human-readable transport identifier, e.g. <c>"redis-pubsub"</c>, <c>"koan-messaging"</c>.</summary>
    string TransportName { get; }

    /// <summary>Declared transport semantics. Used by coordinators to decide whether to call <see cref="CatchUp"/>.</summary>
    CoherenceCapabilities Capabilities { get; }

    /// <summary>
    /// Publish a message to all subscribers. Must not throw on transport failure;
    /// implementations log and return.
    /// </summary>
    ValueTask Publish(TMessage message, CancellationToken ct);

    /// <summary>
    /// Wire a handler once at startup. The channel calls it for every received message
    /// (including the publisher's own — origin filtering is the coordinator's concern).
    /// </summary>
    ValueTask Subscribe(
        Func<TMessage, CancellationToken, ValueTask> onReceived,
        CancellationToken ct);

    /// <summary>
    /// Replay messages newer than <paramref name="cursor"/>. No-op for transports that
    /// declare <c>SupportsCatchUp = false</c>. Returns a new cursor the caller can persist.
    /// </summary>
    ValueTask<string?> CatchUp(
        string? cursor,
        Func<TMessage, CancellationToken, ValueTask> onReceived,
        CancellationToken ct);
}
