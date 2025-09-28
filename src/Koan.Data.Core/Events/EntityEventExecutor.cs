using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Koan.Data.Abstractions;

namespace Koan.Data.Core.Events;

internal static class EntityEventExecutor<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : notnull
{
    public static async Task<TEntity?> ExecuteLoadAsync(Func<CancellationToken, Task<TEntity?>> loader, CancellationToken cancellationToken)
    {
        if (loader == null) throw new ArgumentNullException(nameof(loader));

        var entity = await loader(cancellationToken).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        if (!EntityEventRegistry<TEntity, TKey>.HasLoadPipeline)
        {
            return entity;
        }

        var state = new EntityEventOperationState();
        var context = new EntityEventContext<TEntity>(entity, EntityEventOperation.Load, EntityEventPrior<TEntity>.Empty, state, cancellationToken);
        await RunSetupAsync(context).ConfigureAwait(false);
        context.CaptureProtectionSnapshot();

        var before = await RunBeforeAsync(context, EntityEventOperation.Load).ConfigureAwait(false);
        if (before.IsCancelled)
        {
            throw new EntityEventCancelledException(EntityEventOperation.Load, before.Reason!, before.Code);
        }

        context.ValidateProtection();
        await RunAfterAsync(context, EntityEventOperation.Load).ConfigureAwait(false);
        context.ValidateProtection();
        return context.Current;
    }

    public static async Task<TEntity> ExecuteUpsertAsync(
        TEntity entity,
        Func<TEntity, CancellationToken, Task<TEntity>> persist,
        Func<CancellationToken, ValueTask<TEntity?>> priorLoader,
        CancellationToken cancellationToken)
    {
        if (!EntityEventRegistry<TEntity, TKey>.HasUpsertPipeline)
        {
            return await persist(entity, cancellationToken).ConfigureAwait(false);
        }

        var state = new EntityEventOperationState();
        var prior = new EntityEventPrior<TEntity>(priorLoader);
        var context = new EntityEventContext<TEntity>(entity, EntityEventOperation.Upsert, prior, state, cancellationToken);

        await RunSetupAsync(context).ConfigureAwait(false);
        context.CaptureProtectionSnapshot();

        var before = await RunBeforeAsync(context, EntityEventOperation.Upsert).ConfigureAwait(false);
        if (before.IsCancelled)
        {
            throw new EntityEventCancelledException(EntityEventOperation.Upsert, before.Reason!, before.Code);
        }

        context.ValidateProtection();

        var persisted = await persist(context.Current, cancellationToken).ConfigureAwait(false);
        context.UpdateCurrent(persisted);

        await RunAfterAsync(context, EntityEventOperation.Upsert).ConfigureAwait(false);
        context.ValidateProtection();

        return context.Current;
    }

    public static async Task<int> ExecuteUpsertManyAsync(
        IReadOnlyList<TEntity> entities,
        Func<IReadOnlyList<TEntity>, CancellationToken, Task<int>> persist,
        Func<TEntity, CancellationToken, ValueTask<TEntity?>> priorLoader,
        CancellationToken cancellationToken)
    {
        if (!EntityEventRegistry<TEntity, TKey>.HasUpsertPipeline)
        {
            return await persist(entities, cancellationToken).ConfigureAwait(false);
        }

        var outcomes = new List<EntityOutcome>();
        var executingContexts = new List<EntityEventContext<TEntity>>();
        var payload = new List<TEntity>();
        var requiresAtomic = false;

        foreach (var entity in entities)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var state = new EntityEventOperationState();
            var prior = new EntityEventPrior<TEntity>(ct => priorLoader(entity, ct));
            var context = new EntityEventContext<TEntity>(entity, EntityEventOperation.Upsert, prior, state, cancellationToken);

            await RunSetupAsync(context).ConfigureAwait(false);
            context.CaptureProtectionSnapshot();

            var result = await RunBeforeAsync(context, EntityEventOperation.Upsert).ConfigureAwait(false);
            if (result.IsCancelled)
            {
                outcomes.Add(new EntityOutcome(GetEntityKey(entity), EntityEventOperation.Upsert, result));
                requiresAtomic |= state.IsAtomic;
                if (state.IsAtomic)
                {
                    break;
                }

                continue;
            }

            context.ValidateProtection();
            payload.Add(context.Current);
            executingContexts.Add(context);
            outcomes.Add(new EntityOutcome(GetEntityKey(entity), EntityEventOperation.Upsert, result));
            requiresAtomic |= state.IsAtomic;
        }

        if (requiresAtomic && executingContexts.Count != entities.Count)
        {
            throw new EntityEventBatchCancelledException(EntityEventOperation.Upsert, outcomes);
        }

        var persistedCount = payload.Count > 0
            ? await persist(payload, cancellationToken).ConfigureAwait(false)
            : 0;

        foreach (var context in executingContexts)
        {
            await RunAfterAsync(context, EntityEventOperation.Upsert).ConfigureAwait(false);
            context.ValidateProtection();
        }

        return persistedCount;
    }

    public static async Task<bool> ExecuteRemoveAsync(
        TKey id,
        Func<CancellationToken, Task<TEntity?>> loader,
        Func<TEntity, CancellationToken, Task<bool>> remover,
        CancellationToken cancellationToken)
    {
        if (!EntityEventRegistry<TEntity, TKey>.HasRemovePipeline)
        {
            var entity = await loader(cancellationToken).ConfigureAwait(false);
            if (entity is null) return false;
            return await remover(entity, cancellationToken).ConfigureAwait(false);
        }

        var entityToRemove = await loader(cancellationToken).ConfigureAwait(false);
        if (entityToRemove is null)
        {
            return false;
        }

        var state = new EntityEventOperationState();
        var prior = new EntityEventPrior<TEntity>(_ => new ValueTask<TEntity?>(entityToRemove));
        var context = new EntityEventContext<TEntity>(entityToRemove, EntityEventOperation.Remove, prior, state, cancellationToken);

        await RunSetupAsync(context).ConfigureAwait(false);
        context.CaptureProtectionSnapshot();

        var before = await RunBeforeAsync(context, EntityEventOperation.Remove).ConfigureAwait(false);
        if (before.IsCancelled)
        {
            throw new EntityEventCancelledException(EntityEventOperation.Remove, before.Reason!, before.Code);
        }

        context.ValidateProtection();
        var removed = await remover(context.Current, cancellationToken).ConfigureAwait(false);
        await RunAfterAsync(context, EntityEventOperation.Remove).ConfigureAwait(false);
        context.ValidateProtection();
        return removed;
    }

    public static async Task<int> ExecuteRemoveManyAsync(
        IReadOnlyList<TEntity> existingEntities,
        Func<IReadOnlyList<TEntity>, CancellationToken, Task<int>> remover,
        CancellationToken cancellationToken)
    {
        if (!EntityEventRegistry<TEntity, TKey>.HasRemovePipeline)
        {
            return await remover(existingEntities, cancellationToken).ConfigureAwait(false);
        }

        var outcomes = new List<EntityOutcome>();
        var executingContexts = new List<EntityEventContext<TEntity>>();
        var payload = new List<TEntity>();
        var requiresAtomic = false;

        foreach (var entity in existingEntities)
        {
            var state = new EntityEventOperationState();
            var prior = new EntityEventPrior<TEntity>(_ => new ValueTask<TEntity?>(entity));
            var context = new EntityEventContext<TEntity>(entity, EntityEventOperation.Remove, prior, state, cancellationToken);

            await RunSetupAsync(context).ConfigureAwait(false);
            context.CaptureProtectionSnapshot();

            var result = await RunBeforeAsync(context, EntityEventOperation.Remove).ConfigureAwait(false);
            if (result.IsCancelled)
            {
                outcomes.Add(new EntityOutcome(GetEntityKey(entity), EntityEventOperation.Remove, result));
                requiresAtomic |= state.IsAtomic;
                if (state.IsAtomic)
                {
                    break;
                }

                continue;
            }

            context.ValidateProtection();
            payload.Add(context.Current);
            executingContexts.Add(context);
            outcomes.Add(new EntityOutcome(GetEntityKey(entity), EntityEventOperation.Remove, result));
            requiresAtomic |= state.IsAtomic;
        }

        if (requiresAtomic && executingContexts.Count != existingEntities.Count)
        {
            throw new EntityEventBatchCancelledException(EntityEventOperation.Remove, outcomes);
        }

        var removed = payload.Count > 0
            ? await remover(payload, cancellationToken).ConfigureAwait(false)
            : 0;

        foreach (var context in executingContexts)
        {
            await RunAfterAsync(context, EntityEventOperation.Remove).ConfigureAwait(false);
            context.ValidateProtection();
        }

        return removed;
    }

    private static async Task RunSetupAsync(EntityEventContext<TEntity> context)
    {
        var memory = EntityEventRegistry<TEntity, TKey>.SetupHandlers;
        for (var i = 0; i < memory.Length; i++)
        {
            var handler = memory.Span[i];
            await handler(context).ConfigureAwait(false);
        }
    }

    private static async ValueTask<EntityEventResult> RunBeforeAsync(EntityEventContext<TEntity> context, EntityEventOperation operation)
    {
        ReadOnlyMemory<Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>>> handlers = operation switch
        {
            EntityEventOperation.Load => EntityEventRegistry<TEntity, TKey>.BeforeLoadHandlers,
            EntityEventOperation.Upsert => EntityEventRegistry<TEntity, TKey>.BeforeUpsertHandlers,
            EntityEventOperation.Remove => EntityEventRegistry<TEntity, TKey>.BeforeRemoveHandlers,
            _ => ReadOnlyMemory<Func<EntityEventContext<TEntity>, ValueTask<EntityEventResult>>>.Empty
        };

        var result = EntityEventResult.Proceed();
        for (var i = 0; i < handlers.Length; i++)
        {
            var handler = handlers.Span[i];
            result = await handler(context).ConfigureAwait(false);
            if (result.IsCancelled)
            {
                return result;
            }
        }

        return result;
    }

    private static async Task RunAfterAsync(EntityEventContext<TEntity> context, EntityEventOperation operation)
    {
        ReadOnlyMemory<Func<EntityEventContext<TEntity>, ValueTask>> handlers = operation switch
        {
            EntityEventOperation.Load => EntityEventRegistry<TEntity, TKey>.AfterLoadHandlers,
            EntityEventOperation.Upsert => EntityEventRegistry<TEntity, TKey>.AfterUpsertHandlers,
            EntityEventOperation.Remove => EntityEventRegistry<TEntity, TKey>.AfterRemoveHandlers,
            _ => ReadOnlyMemory<Func<EntityEventContext<TEntity>, ValueTask>>.Empty
        };

        for (var i = 0; i < handlers.Length; i++)
        {
            var handler = handlers.Span[i];
            await handler(context).ConfigureAwait(false);
        }
    }

    private static object? GetEntityKey(TEntity entity) => entity.Id;
}
