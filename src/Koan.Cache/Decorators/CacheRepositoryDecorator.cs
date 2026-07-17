using System;
using Koan.Cache.Abstractions.Stores;
using Koan.Cache.Entity;
using Koan.Cache.Stores;
using Koan.Core;
using Koan.Data.Core.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.Cache.Decorators;

/// <summary>
/// Repository decorator that applies <c>[Cacheable]</c> / <c>[CachePolicy]</c> intent to any
/// <c>IDataRepository&lt;T,K&gt;</c>. <c>[ProviderPriority(100)]</c> places this in the
/// "read short-circuit" band — cache hits return before downstream decorators (CQRS, audit)
/// observe the read. ARCH-0076 (M10) documents the canonical priority bands.
/// </summary>
[ProviderPriority(100)]
internal sealed class CacheRepositoryDecorator : IDataRepositoryDecorator
{
    private readonly EntityCachePlan _plans;
    private readonly ILogger<CacheRepositoryDecorator> _logger;

    public CacheRepositoryDecorator(EntityCachePlan plans, ILogger<CacheRepositoryDecorator> logger)
    {
        _plans = plans ?? throw new ArgumentNullException(nameof(plans));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public object? TryDecorate(Type entityType, Type keyType, object repository, IServiceProvider services)
    {
        if (repository is null)
        {
            return null;
        }

        var plan = _plans.TryResolve(entityType);
        if (plan is null)
        {
            return null;
        }

        if (plan.ExclusionReason is not null)
        {
            _logger.LogInformation(
                "[Cacheable] {Entity} excluded from cache: {Reason}.",
                plan.EntityName,
                plan.ExclusionReason);
            return null;
        }

        if (services.GetService(typeof(CacheClient)) is not CacheClient cacheClient)
        {
            _logger.LogWarning("Cache policy detected for entity {EntityType} but no ICacheClient is registered. Skipping cache decoration.", entityType);
            return null;
        }

        var decoratorType = typeof(CachedRepository<,>).MakeGenericType(entityType, keyType);
        var decorated = ActivatorUtilities.CreateInstance(services, decoratorType, repository, cacheClient, plan);
        _logger.LogDebug(
            "Cache decorator applied to repository for {EntityType} using strategy {Strategy}.",
            entityType.Name,
            plan.Policy.Strategy);
        return decorated;
    }
}
