using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Koan.Canon.Core.Infrastructure;
using Koan.Data.Core.Model;

namespace Koan.Canon.Core.Interceptors;

/// <summary>
/// Registry for managing Canon interceptors for a specific entity type
/// </summary>
/// <typeparam name="T">The Canon entity type</typeparam>
internal class CanonInterceptorRegistry<T> : ICanonInterceptorRegistry where T : class
{
    private readonly ILogger<CanonInterceptorRegistry<T>> _logger;
    
    // Before-stage interceptors
    private readonly List<Func<T, Task<CanonIntakeAction>>> _beforeIntake = new();
    private readonly List<Func<T, Task<CanonStageAction>>> _beforeKeying = new();
    private readonly List<Func<T, Task<CanonStageAction>>> _beforeAssociation = new();
    private readonly List<Func<T, Task<CanonStageAction>>> _beforeProjection = new();
    private readonly List<Func<T, Task<CanonStageAction>>> _beforeMaterialization = new();
    
    // After-stage interceptors
    private readonly List<Func<T, Task<CanonStageAction>>> _afterIntake = new();
    private readonly List<Func<T, Task<CanonStageAction>>> _afterKeying = new();
    private readonly List<Func<T, Task<CanonStageAction>>> _afterAssociation = new();
    private readonly List<Func<T, Task<CanonStageAction>>> _afterProjection = new();
    private readonly List<Func<T, Task<CanonStageAction>>> _afterMaterialization = new();
    
    // Conditional interceptors
    private readonly List<Func<T, Task<CanonStageAction>>> _onAssociationSuccess = new();
    private readonly List<Func<T, Task<CanonStageAction>>> _onAssociationFailure = new();
    
    private readonly object _lock = new object();

    public CanonInterceptorRegistry(ILogger<CanonInterceptorRegistry<T>> logger)
    {
        _logger = logger;
    }

    #region Registration Methods
    
    public void RegisterBeforeIntake(Func<T, Task<CanonIntakeAction>> interceptor)
    {
        lock (_lock)
        {
            _beforeIntake.Add(interceptor);
        }
    }
    
    public void RegisterAfterIntake(Func<T, Task<CanonStageAction>> interceptor)
    {
        lock (_lock)
        {
            _afterIntake.Add(interceptor);
        }
    }
    
    public void RegisterBeforeKeying(Func<T, Task<CanonStageAction>> interceptor)
    {
        lock (_lock)
        {
            _beforeKeying.Add(interceptor);
        }
    }
    
    public void RegisterAfterKeying(Func<T, Task<CanonStageAction>> interceptor)
    {
        lock (_lock)
        {
            _afterKeying.Add(interceptor);
        }
    }
    
    public void RegisterBeforeAssociation(Func<T, Task<CanonStageAction>> interceptor)
    {
        lock (_lock)
        {
            _beforeAssociation.Add(interceptor);
        }
    }
    
    public void RegisterAfterAssociation(Func<T, Task<CanonStageAction>> interceptor)
    {
        lock (_lock)
        {
            _afterAssociation.Add(interceptor);
        }
    }
    
    public void RegisterBeforeProjection(Func<T, Task<CanonStageAction>> interceptor)
    {
        lock (_lock)
        {
            _beforeProjection.Add(interceptor);
        }
    }
    
    public void RegisterAfterProjection(Func<T, Task<CanonStageAction>> interceptor)
    {
        lock (_lock)
        {
            _afterProjection.Add(interceptor);
        }
    }
    
    public void RegisterBeforeMaterialization(Func<T, Task<CanonStageAction>> interceptor)
    {
        lock (_lock)
        {
            _beforeMaterialization.Add(interceptor);
        }
    }
    
    public void RegisterAfterMaterialization(Func<T, Task<CanonStageAction>> interceptor)
    {
        lock (_lock)
        {
            _afterMaterialization.Add(interceptor);
        }
    }
    
    public void RegisterOnAssociationSuccess(Func<T, Task<CanonStageAction>> interceptor)
    {
        lock (_lock)
        {
            _onAssociationSuccess.Add(interceptor);
        }
    }
    
    public void RegisterOnAssociationFailure(Func<T, Task<CanonStageAction>> interceptor)
    {
        lock (_lock)
        {
            _onAssociationFailure.Add(interceptor);
        }
    }
    
    #endregion

    #region Query Methods
    
    public bool HasBeforeIntake()
    {
        lock (_lock)
        {
            return _beforeIntake.Count > 0;
        }
    }
    
    #endregion

    #region Execution Methods
    
    public async Task<CanonIntakeAction?> ExecuteBeforeIntake(T entity)
    {
        List<Func<T, Task<CanonIntakeAction>>> interceptors;
        lock (_lock)
        {
            if (_beforeIntake.Count == 0) return null;
            interceptors = new List<Func<T, Task<CanonIntakeAction>>>(_beforeIntake);
        }

        foreach (var interceptor in interceptors)
        {
            try
            {
                var result = await interceptor(entity);
                if (result.ShouldStop)
                {
                    _logger.LogDebug("BeforeIntake interceptor returned {Action} for {EntityType}: {Reason}", 
                        result.Action, typeof(T).Name, result.Reason);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BeforeIntake interceptor failed for entity type {EntityType}", typeof(T).Name);
                // Continue with other interceptors - don't fail the entire pipeline
            }
        }
        return null;
    }
    
    public async Task<CanonStageAction?> ExecuteAfterIntake(T entity)
    {
        return await ExecuteStageInterceptors(_afterIntake, entity, "AfterIntake");
    }
    
    public async Task<CanonStageAction?> ExecuteBeforeKeying(T entity)
    {
        return await ExecuteStageInterceptors(_beforeKeying, entity, "BeforeKeying");
    }
    
    public async Task<CanonStageAction?> ExecuteAfterKeying(T entity)
    {
        return await ExecuteStageInterceptors(_afterKeying, entity, "AfterKeying");
    }
    
    public async Task<CanonStageAction?> ExecuteBeforeAssociation(T entity)
    {
        return await ExecuteStageInterceptors(_beforeAssociation, entity, "BeforeAssociation");
    }
    
    public async Task<CanonStageAction?> ExecuteAfterAssociation(T entity)
    {
        return await ExecuteStageInterceptors(_afterAssociation, entity, "AfterAssociation");
    }
    
    public async Task<CanonStageAction?> ExecuteOnAssociationSuccess(T entity)
    {
        return await ExecuteStageInterceptors(_onAssociationSuccess, entity, "OnAssociationSuccess");
    }
    
    public async Task<CanonStageAction?> ExecuteOnAssociationFailure(T entity)
    {
        return await ExecuteStageInterceptors(_onAssociationFailure, entity, "OnAssociationFailure");
    }
    
    public async Task<CanonStageAction?> ExecuteBeforeProjection(T entity)
    {
        return await ExecuteStageInterceptors(_beforeProjection, entity, "BeforeProjection");
    }
    
    public async Task<CanonStageAction?> ExecuteAfterProjection(T entity)
    {
        return await ExecuteStageInterceptors(_afterProjection, entity, "AfterProjection");
    }
    
    public async Task<CanonStageAction?> ExecuteBeforeMaterialization(T entity)
    {
        return await ExecuteStageInterceptors(_beforeMaterialization, entity, "BeforeMaterialization");
    }
    
    public async Task<CanonStageAction?> ExecuteAfterMaterialization(T entity)
    {
        return await ExecuteStageInterceptors(_afterMaterialization, entity, "AfterMaterialization");
    }
    
    private async Task<CanonStageAction?> ExecuteStageInterceptors(
        List<Func<T, Task<CanonStageAction>>> interceptors, 
        T entity, 
        string stageName)
    {
        List<Func<T, Task<CanonStageAction>>> interceptorsCopy;
        lock (_lock)
        {
            if (interceptors.Count == 0) return null;
            interceptorsCopy = new List<Func<T, Task<CanonStageAction>>>(interceptors);
        }

        foreach (var interceptor in interceptorsCopy)
        {
            try
            {
                var result = await interceptor(entity);
                if (result.ShouldStop)
                {
                    _logger.LogDebug("{StageName} interceptor returned {Action} for {EntityType}: {Reason}", 
                        stageName, result.ActionType, typeof(T).Name, result.Reason);
                    return result;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{StageName} interceptor failed for entity type {EntityType}", stageName, typeof(T).Name);
                // Continue with other interceptors - don't fail the entire pipeline
            }
        }
        return null;
    }
    
    #endregion
    
    /// <summary>
    /// Check if this registry has any interceptors registered
    /// </summary>
    public bool HasInterceptors()
    {
        lock (_lock)
        {
            return _beforeIntake.Count > 0 || _afterIntake.Count > 0 ||
                   _beforeKeying.Count > 0 || _afterKeying.Count > 0 ||
                   _beforeAssociation.Count > 0 || _afterAssociation.Count > 0 ||
                   _beforeProjection.Count > 0 || _afterProjection.Count > 0 ||
                   _beforeMaterialization.Count > 0 || _afterMaterialization.Count > 0 ||
                   _onAssociationSuccess.Count > 0 || _onAssociationFailure.Count > 0;
        }
    }
    
    /// <summary>
    /// Non-generic implementation for runtime type access
    /// </summary>
    public bool HasBeforeIntakeNonGeneric()
    {
        return HasBeforeIntake();
    }
    
    /// <summary>
    /// Non-generic implementation for runtime type access
    /// </summary>
    public async Task<object?> ExecuteBeforeIntakeNonGeneric(object entity)
    {
        if (entity is T typedEntity)
        {
            return await ExecuteBeforeIntake(typedEntity);
        }
        return null;
    }
}

