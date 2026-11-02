using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Core.Context;
using Koan.Data.Abstractions;
using Koan.Data.Core.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Koan.Data.Core.Transactions;

/// <summary>
/// Represents a deferred entity operation that can be executed within a transaction.
/// </summary>
internal interface ITrackedOperation
{
    /// <summary>
    /// Execute the operation using the repository.
    /// </summary>
    Task Execute(CancellationToken ct);

    /// <summary>
    /// Get the adapter hint for this operation (for grouping).
    /// </summary>
    string GetAdapterHint();

    DataRouteBinding? ResolveDataBinding(IServiceProvider services);

    /// <summary>
    /// Provision and validate the operation's store under the context it was issued in, outside any transaction, so
    /// an atomic commit opens its native transaction over a schema that is already there (DATA-0158).
    /// </summary>
    Task EnsureReady(CancellationToken ct);
}

/// <summary>
/// The context an operation was issued in (NK-047): every registered context carrier (tenant, data axes and other
/// ambient scopes) captured when the operation was tracked, restored exactly when it is resolved and executed at
/// commit, so a deferred write lands where it would have landed had it run immediately, whatever the commit ambient.
/// </summary>
internal readonly record struct CapturedCarriers(
    KoanContextCarrierRegistry? Registry,
    IReadOnlyDictionary<string, string>? Values)
{
    public static CapturedCarriers Capture(IServiceProvider services)
    {
        var registry = services.GetService(typeof(KoanContextCarrierRegistry)) as KoanContextCarrierRegistry;
        return new CapturedCarriers(registry, registry?.Capture());
    }

    /// <summary>Restore the captured carriers, then the operation's routing, outside any transaction.</summary>
    public IDisposable Enter(EntityContext.ContextState context)
    {
        var carriers = Registry?.Restore(Values, ContextIngressTrust.HostTrusted);
        try
        {
            var routing = EntityContext.With(
                source: context.Source ?? "",
                adapter: context.Adapter ?? "",
                partition: context.Partition ?? "",
                preserveTransaction: false);
            return new Scope(routing, carriers);
        }
        catch
        {
            carriers?.Dispose();
            throw;
        }
    }

    private sealed class Scope(IDisposable routing, IDisposable? carriers) : IDisposable
    {
        public void Dispose()
        {
            try { routing.Dispose(); }
            finally { carriers?.Dispose(); }
        }
    }
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
    private readonly CapturedCarriers _carriers;

    public SaveOperation(TEntity entity, EntityContext.ContextState context, CapturedCarriers carriers)
    {
        _entity = entity ?? throw new ArgumentNullException(nameof(entity));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _carriers = carriers;
    }

    public async Task Execute(CancellationToken ct)
    {
        using var _ = _carriers.Enter(_context);
        await Data<TEntity, TKey>.Upsert(_entity, ct).ConfigureAwait(false);
    }

    public string GetAdapterHint()
    {
        return _context.Adapter ?? _context.Source ?? "Default";
    }

    public DataRouteBinding? ResolveDataBinding(IServiceProvider services)
    {
        using var _ = _carriers.Enter(_context);
        return AdapterResolver.ResolveDecisionForEntity<TEntity>(
            services,
            services.GetRequiredService<DataSourceRegistry>()).Binding;
    }

    public async Task EnsureReady(CancellationToken ct)
    {
        using var _ = _carriers.Enter(_context);
        await Koan.Core.Hosting.App.AppHost.GetRequiredService<IDataService>("deferred transaction readiness")
            .GetRepository<TEntity, TKey>().EnsureReady(ct).ConfigureAwait(false);
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
    private readonly CapturedCarriers _carriers;

    public DeleteOperation(TKey id, EntityContext.ContextState context, CapturedCarriers carriers)
    {
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _carriers = carriers;
    }

    public async Task Execute(CancellationToken ct)
    {
        using var _ = _carriers.Enter(_context);
        await Data<TEntity, TKey>.Delete(_id, ct).ConfigureAwait(false);
    }

    public string GetAdapterHint()
    {
        return _context.Adapter ?? _context.Source ?? "Default";
    }

    public DataRouteBinding? ResolveDataBinding(IServiceProvider services)
    {
        using var _ = _carriers.Enter(_context);
        return AdapterResolver.ResolveDecisionForEntity<TEntity>(
            services,
            services.GetRequiredService<DataSourceRegistry>()).Binding;
    }

    public async Task EnsureReady(CancellationToken ct)
    {
        using var _ = _carriers.Enter(_context);
        await Koan.Core.Hosting.App.AppHost.GetRequiredService<IDataService>("deferred transaction readiness")
            .GetRepository<TEntity, TKey>().EnsureReady(ct).ConfigureAwait(false);
    }
}
