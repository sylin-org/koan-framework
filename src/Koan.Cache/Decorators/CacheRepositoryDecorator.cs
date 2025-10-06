using System;
using System.Linq;
using Koan.Cache.Abstractions.Policies;
using Koan.Cache.Abstractions.Primitives;
using Koan.Cache.Abstractions.Stores;
using Koan.Data.Core.Decorators;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Koan.Cache.Decorators;

internal sealed class CacheRepositoryDecorator : IDataRepositoryDecorator
{
    private readonly ICachePolicyRegistry _policyRegistry;
    private readonly ILogger<CacheRepositoryDecorator> _logger;

    public CacheRepositoryDecorator(ICachePolicyRegistry policyRegistry, ILogger<CacheRepositoryDecorator> logger)
    {
        _policyRegistry = policyRegistry ?? throw new ArgumentNullException(nameof(policyRegistry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public object? TryDecorate(Type entityType, Type keyType, object repository, IServiceProvider services)
    {
        if (repository is null)
        {
            return null;
        }

        var policies = _policyRegistry.GetPoliciesFor(entityType);
        if (policies.Count == 0)
        {
            return null;
        }

        var entityPolicy = policies.FirstOrDefault(p => p.Scope == CacheScope.Entity && p.Strategy != CacheStrategy.NoCache);
        if (entityPolicy is null)
        {
            return null;
        }

        if (services.GetService(typeof(ICacheClient)) is not ICacheClient cacheClient)
        {
            _logger.LogWarning("Cache policy detected for entity {EntityType} but no ICacheClient is registered. Skipping cache decoration.", entityType);
            return null;
        }

        var decoratorType = typeof(CachedRepository<,>).MakeGenericType(entityType, keyType);
        var decorated = ActivatorUtilities.CreateInstance(services, decoratorType, repository, cacheClient, entityPolicy);
        _logger.LogDebug("Cache decorator applied to repository for {EntityType} using strategy {Strategy}.", entityType.Name, entityPolicy.Strategy);
        return decorated;
    }
}
