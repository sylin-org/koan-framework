using System;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Transactions;

/// <summary>
/// Represents a deferred entity operation that can be executed within a transaction.
/// </summary>
internal interface ITrackedOperation
{
    /// <summary>
    /// Execute the operation using the repository.
    /// </summary>
    Task ExecuteAsync(CancellationToken ct);

    /// <summary>
    /// Get the adapter hint for this operation (for grouping).
    /// </summary>
    string GetAdapterHint();
}

/// <summary>
/// Tracked entity save operation.
/// </summary>
internal sealed class SaveOperation<TEntity, TKey> : ITrackedOperation
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly TEntity _entity;
    private readonly EntityContext.ContextState _context;
    private readonly string? _partition;

    public SaveOperation(TEntity entity, EntityContext.ContextState context, string? partition = null)
    {
        _entity = entity ?? throw new ArgumentNullException(nameof(entity));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _partition = partition;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var partition = _partition ?? _context.Partition;

        using var _ = EntityContext.With(
            source: string.IsNullOrWhiteSpace(_context.Source) ? null : _context.Source,
            adapter: string.IsNullOrWhiteSpace(_context.Adapter) ? null : _context.Adapter,
            partition: partition);

        await Data<TEntity, TKey>.UpsertAsync(_entity, ct);
    }

    public string GetAdapterHint()
    {
        return _context.Adapter ?? _context.Source ?? "Default";
    }
}

/// <summary>
/// Tracked entity delete operation.
/// </summary>
internal sealed class DeleteOperation<TEntity, TKey> : ITrackedOperation
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly TKey _id;
    private readonly EntityContext.ContextState _context;
    private readonly string? _partition;

    public DeleteOperation(TKey id, EntityContext.ContextState context, string? partition = null)
    {
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _partition = partition;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        var partition = _partition ?? _context.Partition;

        using var _ = EntityContext.With(
            source: string.IsNullOrWhiteSpace(_context.Source) ? null : _context.Source,
            adapter: string.IsNullOrWhiteSpace(_context.Adapter) ? null : _context.Adapter,
            partition: partition);

        await Data<TEntity, TKey>.DeleteAsync(_id, ct);
    }

    public string GetAdapterHint()
    {
        return _context.Adapter ?? _context.Source ?? "Default";
    }
}
