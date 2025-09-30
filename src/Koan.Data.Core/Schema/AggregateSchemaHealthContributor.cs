using System;
using Koan.Data.Abstractions;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Data.Core.Schema;

internal sealed class AggregateSchemaHealthContributor<TEntity, TKey> : ISchemaHealthContributor<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    private readonly IServiceProvider _services;

    public AggregateSchemaHealthContributor(IServiceProvider services)
    {
        _services = services;
    }

    public async Task EnsureHealthyAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
    var repo = global::Koan.Data.Core.AggregateConfigs.Get<TEntity, TKey>(_services).Repository;
        if (repo is ISchemaHealthContributor<TEntity, TKey> contributor)
        {
            await contributor.EnsureHealthyAsync(ct).ConfigureAwait(false);
        }
    }

    public void InvalidateHealth()
    {
    var repo = global::Koan.Data.Core.AggregateConfigs.Get<TEntity, TKey>(_services).Repository;
        if (repo is ISchemaHealthContributor<TEntity, TKey> contributor)
        {
            contributor.InvalidateHealth();
        }
    }
}
