using Koan.Cache.Abstractions.Stores;
using Koan.Core.Context;
using Koan.Data.Core;
using Koan.Data.Core.Model;
using Koan.Cache.Stores;
using Koan.Cache.Identity;

namespace Koan.Cache.Entity;

/// <summary>Executes one bounded, context-stable Entity cache eviction source.</summary>
internal sealed class EntityCacheEvictionCoordinator(
    ICacheSubjectClient writer,
    EntityCachePlan plans,
    KoanContextCarrierRegistry contextCarriers)
{
    public async Task<EntityCacheEviction> Evict<TEntity, TKey>(
        IAsyncEnumerable<Entity<TEntity, TKey>> entities,
        CancellationToken ct)
        where TEntity : class, Koan.Data.Abstractions.IEntity<TKey>
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(entities);

        // Resolve applicability and capture every ambient key axis before deferred enumeration begins.
        var plan = plans.Require(typeof(TEntity));
        var capturedDataContext = EntityContext.Current;
        var capturedCarriers = contextCarriers.Capture();
        var enumerated = 0L;
        var removed = 0L;
        var absent = 0L;
        var skipped = 0L;
        var failed = 0L;
        var sourceCompleted = false;
        var enumerating = true;
        var currentConfirmed = false;

        EntityCacheEviction Snapshot()
            => new(typeof(TEntity), enumerated, removed, absent, skipped, failed, sourceCompleted);

        try
        {
            using var operationContext = EnterContext(capturedDataContext, capturedCarriers);
            await foreach (var entity in entities.WithCancellation(ct).ConfigureAwait(false))
            {
                enumerating = false;
                currentConfirmed = false;
                enumerated++;

                var id = entity.Id;
                if (EqualityComparer<TKey>.Default.Equals(id, default!))
                {
                    skipped++;
                    currentConfirmed = true;
                    enumerating = true;
                    continue;
                }

                if (!plan.TryBuildKey(entity, id, out var key))
                {
                    throw new InvalidOperationException(
                        $"Cache policy for '{typeof(TEntity).Name}' could not resolve key template " +
                        $"'{plan.Policy.KeyTemplate}' from the supplied Entity and captured context.");
                }

                bool existed;
                using (EnterContext(capturedDataContext, capturedCarriers))
                {
                    existed = await writer.Remove(key, typeof(TEntity), ct).ConfigureAwait(false);
                }

                if (existed)
                {
                    removed++;
                }
                else
                {
                    absent++;
                }

                currentConfirmed = true;
                enumerating = true;
            }

            sourceCompleted = true;
            return Snapshot();
        }
        catch (EntityCacheEvictionException)
        {
            throw;
        }
        catch (EntityCacheEvictionCanceledException)
        {
            throw;
        }
        catch (OperationCanceledException error) when (ct.IsCancellationRequested)
        {
            if (!enumerating && !currentConfirmed)
            {
                failed++;
            }

            throw new EntityCacheEvictionCanceledException(
                $"Cache eviction for '{typeof(TEntity).Name}' was canceled; the outcome reports the confirmed prefix.",
                Snapshot(),
                error,
                ct);
        }
        catch (Exception error)
        {
            if (!enumerating && !currentConfirmed)
            {
                failed++;
            }

            var failure = enumerating
                ? EntityCacheEvictionException.FailureKind.SourceFailed
                : EntityCacheEvictionException.FailureKind.EvictionFailed;
            var message = failure == EntityCacheEvictionException.FailureKind.SourceFailed
                ? $"The cache eviction source for '{typeof(TEntity).Name}' failed after {removed + absent} confirmed removal(s)."
                : $"Cache eviction for '{typeof(TEntity).Name}' failed at source ordinal {Math.Max(0, enumerated - 1)}; " +
                  "the outcome reports confirmed removals only.";
            throw new EntityCacheEvictionException(failure, message, Snapshot(), error);
        }
    }

    private IDisposable EnterContext(
        EntityContext.ContextState? dataContext,
        IReadOnlyDictionary<string, string>? carriers)
    {
        var carrierScope = contextCarriers.Restore(carriers, ContextIngressTrust.HostTrusted);
        try
        {
            var dataScope = dataContext is null
                ? KoanContext.Suppress<EntityContext.ContextState>()
                : KoanContext.Push(dataContext);
            return new ContextScope(dataScope, carrierScope);
        }
        catch
        {
            carrierScope.Dispose();
            throw;
        }
    }

    private sealed class ContextScope(IDisposable data, IDisposable carriers) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                data.Dispose();
            }
            finally
            {
                carriers.Dispose();
            }
        }
    }
}
