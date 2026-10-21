using Koan.Core.Hosting.App;
using Koan.Data.Abstractions;
using Koan.Data.Core.Transfers;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core;

public static class EntityPartitionMoveExtensions
{
    /// <summary>
    /// Writes <paramref name="model"/> into <paramref name="toPartition"/> and, unless <paramref name="copy"/> is set,
    /// removes it from its source: <paramref name="fromPartition"/>, else the ambient partition, else the base set.
    /// A source that resolves to the target's own container is refused before anything is written. The result does not
    /// say whether a source row was removed; <see cref="TryMoveToPartition{TEntity}"/> does.
    /// </summary>
    public static Task MoveToPartition<TEntity>(
        this TEntity model,
        string toPartition,
        string? fromPartition = null,
        bool copy = false,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>
        => MoveToPartition<TEntity, string>(model, toPartition, fromPartition, copy, ct);

    /// <inheritdoc cref="MoveToPartition{TEntity}(TEntity, string, string?, bool, CancellationToken)"/>
    public static Task MoveToPartition<TEntity, TKey>(
        this TEntity model,
        string toPartition,
        string? fromPartition = null,
        bool copy = false,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
        => TryMoveToPartition<TEntity, TKey>(model, toPartition, fromPartition, copy, ct);

    /// <summary>
    /// <see cref="MoveToPartition{TEntity}(TEntity, string, string?, bool, CancellationToken)"/> that reports its
    /// outcome: <see langword="false"/> when a move found no row to remove in its source (the row is still written to
    /// the target), <see langword="true"/> otherwise and for every copy.
    /// </summary>
    public static Task<bool> TryMoveToPartition<TEntity>(
        this TEntity model,
        string toPartition,
        string? fromPartition = null,
        bool copy = false,
        CancellationToken ct = default)
        where TEntity : class, IEntity<string>
        => TryMoveToPartition<TEntity, string>(model, toPartition, fromPartition, copy, ct);

    /// <inheritdoc cref="TryMoveToPartition{TEntity}(TEntity, string, string?, bool, CancellationToken)"/>
    public static async Task<bool> TryMoveToPartition<TEntity, TKey>(
        this TEntity model,
        string toPartition,
        string? fromPartition = null,
        bool copy = false,
        CancellationToken ct = default)
        where TEntity : class, IEntity<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(toPartition);

        // An empty partition is the base set: EntityContext.Partition("") clears an ambient partition.
        var from = fromPartition ?? EntityContext.Current?.Partition ?? string.Empty;
        DemandDistinct<TEntity>(from, toPartition);

        await Data<TEntity, TKey>.Upsert(model, toPartition, ct).ConfigureAwait(false);
        if (copy) return true;
        return await Data<TEntity, TKey>.Delete(model.Id, from, ct).ConfigureAwait(false);
    }

    private static void DemandDistinct<TEntity>(string from, string to) where TEntity : class
    {
        var services = AppHost.Current
            ?? throw new InvalidOperationException("A running Koan host is required to move an Entity between partitions.");
        var sources = services.GetRequiredService<DataSourceRegistry>();
        TransferPlace origin, destination;
        using (EntityContext.Partition(from)) origin = TransferPlace.Resolve<TEntity>(services, sources);
        using (EntityContext.Partition(to)) destination = TransferPlace.Resolve<TEntity>(services, sources);
        if (!origin.IsSameAs(destination)) return;

        throw new ArgumentException(
            $"Moving '{typeof(TEntity).Name}' from {(from.Length == 0 ? "the base set" : $"partition '{from}'")} to " +
            $"partition '{to}' was refused: both resolve to container '{destination.Container}', so the move would " +
            "delete the row it just wrote. Pass a different target, or fromPartition when the ambient partition is " +
            "not the row's source; partitions that differ only by case share a container.",
            "toPartition");
    }
}
