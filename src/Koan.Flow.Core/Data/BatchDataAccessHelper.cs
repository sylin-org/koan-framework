using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Koan.Data.Core;

namespace Koan.Flow.Core.Data;

/// <summary>
/// High-performance data access helper with compiled delegates and batch operations.
/// Eliminates reflection overhead and enables bulk database operations for Flow services.
/// </summary>
public static class BatchDataAccessHelper
{
    private static readonly ConcurrentDictionary<Type, Delegate> _getAsyncCache = new();
    private static readonly ConcurrentDictionary<Type, Delegate> _getManyAsyncCache = new();
    private static readonly ConcurrentDictionary<Type, Delegate> _upsertAsyncCache = new();
    private static readonly ConcurrentDictionary<Type, Delegate> _upsertManyAsyncCache = new();
    private static readonly ConcurrentDictionary<Type, Delegate> _deleteManyAsyncCache = new();
    private static readonly ConcurrentDictionary<Type, Delegate> _firstPageAsyncCache = new();
    private static readonly ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    /// <summary>
    /// Generic batch get operation with compiled delegates for maximum performance.
    /// </summary>
    public static async Task<IEnumerable<T>> GetManyAsync<T, TKey>(IEnumerable<TKey> ids, CancellationToken ct = default)
        where T : class, Koan.Data.Abstractions.IEntity<TKey>
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return Enumerable.Empty<T>();

        try
        {
            // Check if Data<T, TKey> has GetManyAsync method
            var dataType = typeof(Data<,>).MakeGenericType(typeof(T), typeof(TKey));
            var getManyMethod = dataType.GetMethod("GetManyAsync", BindingFlags.Public | BindingFlags.Static,
                new[] { typeof(IEnumerable<TKey>), typeof(CancellationToken) });

            if (getManyMethod != null)
            {
                // Use native GetManyAsync if available
                var task = (Task)getManyMethod.Invoke(null, new object[] { idList, ct })!;
                await task.ConfigureAwait(false);
                return (IEnumerable<T>)GetTaskResult(task)!;
            }
            else
            {
                // Fallback to parallel individual GetAsync calls
                return await GetManyAsyncFallback<T, TKey>(idList, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BatchDataAccess] Error in GetManyAsync for {EntityType}", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Generic batch upsert operation for maximum performance.
    /// </summary>
    public static async Task UpsertManyAsync<T, TKey>(IEnumerable<T> entities, string? setName = null, CancellationToken ct = default)
        where T : class, Koan.Data.Abstractions.IEntity<TKey>
    {
        var entityList = entities.ToList();
        if (entityList.Count == 0) return;

        try
        {
            // Check if Data<T, TKey> has UpsertManyAsync method
            var dataType = typeof(Data<,>).MakeGenericType(typeof(T), typeof(TKey));
            var upsertManyMethod = dataType.GetMethod("UpsertManyAsync", BindingFlags.Public | BindingFlags.Static,
                new[] { typeof(IEnumerable<T>), typeof(string), typeof(CancellationToken) });

            if (upsertManyMethod != null)
            {
                // Use native UpsertManyAsync if available
                var task = (Task)upsertManyMethod.Invoke(null, new object?[] { entityList, setName, ct })!;
                await task.ConfigureAwait(false);
            }
            else
            {
                // Fallback to parallel individual UpsertAsync calls
                await UpsertManyAsyncFallback<T, TKey>(entityList, setName, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BatchDataAccess] Error in UpsertManyAsync for {EntityType}", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Generic batch delete operation for maximum performance.
    /// </summary>
    public static async Task DeleteManyAsync<T, TKey>(IEnumerable<TKey> ids, string? setName = null, CancellationToken ct = default)
        where T : class, Koan.Data.Abstractions.IEntity<TKey>
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return;

        try
        {
            // Check if Data<T, TKey> has DeleteManyAsync method
            var dataType = typeof(Data<,>).MakeGenericType(typeof(T), typeof(TKey));
            var deleteManyMethod = dataType.GetMethod("DeleteManyAsync", BindingFlags.Public | BindingFlags.Static,
                new[] { typeof(IEnumerable<TKey>), typeof(string), typeof(CancellationToken) });

            if (deleteManyMethod != null)
            {
                // Use native DeleteManyAsync if available
                var task = (Task)deleteManyMethod.Invoke(null, new object?[] { idList, setName, ct })!;
                await task.ConfigureAwait(false);
            }
            else
            {
                // Fallback to parallel individual DeleteAsync calls
                await DeleteManyAsyncFallback<T, TKey>(idList, setName, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BatchDataAccess] Error in DeleteManyAsync for {EntityType}", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Reflection-based batch get operation for dynamic scenarios.
    /// Uses compiled delegates to minimize reflection overhead.
    /// </summary>
    public static async Task<object[]> GetManyAsync(Type entityType, Type keyType, IEnumerable<object> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return Array.Empty<object>();

        try
        {
            var cacheKey = GenerateCacheKey(entityType, keyType, "GetMany");
            
            if (!_getManyAsyncCache.TryGetValue(entityType, out var cachedDelegate))
            {
                cachedDelegate = CreateGetManyDelegate(entityType, keyType);
                _getManyAsyncCache.TryAdd(entityType, cachedDelegate);
            }

            if (cachedDelegate != null)
            {
                var task = (Task)cachedDelegate.DynamicInvoke(idList, ct)!;
                await task.ConfigureAwait(false);
                var result = GetTaskResult(task);
                return result is System.Collections.IEnumerable enumerable 
                    ? enumerable.Cast<object>().ToArray() 
                    : Array.Empty<object>();
            }

            // Fallback to reflection
            return await GetManyAsyncReflection(entityType, keyType, idList, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BatchDataAccess] Error in dynamic GetManyAsync for {EntityType}", entityType.Name);
            throw;
        }
    }

    /// <summary>
    /// Reflection-based batch upsert operation for dynamic scenarios.
    /// </summary>
    public static async Task UpsertManyAsync(Type entityType, Type keyType, IEnumerable<object> entities, string? setName = null, CancellationToken ct = default)
    {
        var entityList = entities.ToList();
        if (entityList.Count == 0) return;

        try
        {
            var cacheKey = GenerateCacheKey(entityType, keyType, "UpsertMany");
            
            if (!_upsertManyAsyncCache.TryGetValue(entityType, out var cachedDelegate))
            {
                cachedDelegate = CreateUpsertManyDelegate(entityType, keyType);
                _upsertManyAsyncCache.TryAdd(entityType, cachedDelegate);
            }

            if (cachedDelegate != null)
            {
                var task = (Task)cachedDelegate.DynamicInvoke(entityList, setName, ct)!;
                await task.ConfigureAwait(false);
            }
            else
            {
                // Fallback to reflection
                await UpsertManyAsyncReflection(entityType, keyType, entityList, setName, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BatchDataAccess] Error in dynamic UpsertManyAsync for {EntityType}", entityType.Name);
            throw;
        }
    }

    /// <summary>
    /// Reflection-based batch delete operation for dynamic scenarios.
    /// </summary>
    public static async Task DeleteManyAsync(Type entityType, Type keyType, IEnumerable<object> ids, string? setName = null, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        if (idList.Count == 0) return;

        try
        {
            var cacheKey = GenerateCacheKey(entityType, keyType, "DeleteMany");
            
            if (!_deleteManyAsyncCache.TryGetValue(entityType, out var cachedDelegate))
            {
                cachedDelegate = CreateDeleteManyDelegate(entityType, keyType);
                _deleteManyAsyncCache.TryAdd(entityType, cachedDelegate);
            }

            if (cachedDelegate != null)
            {
                var task = (Task)cachedDelegate.DynamicInvoke(idList, setName, ct)!;
                await task.ConfigureAwait(false);
            }
            else
            {
                // Fallback to reflection
                await DeleteManyAsyncReflection(entityType, keyType, idList, setName, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BatchDataAccess] Error in dynamic DeleteManyAsync for {EntityType}", entityType.Name);
            throw;
        }
    }

    /// <summary>
    /// Optimized FirstPage operation with caching.
    /// </summary>
    public static async Task<IEnumerable<object>> FirstPageAsync(Type entityType, Type keyType, int pageSize, CancellationToken ct = default)
    {
        try
        {
            var cacheKey = GenerateCacheKey(entityType, keyType, "FirstPage");
            
            if (!_firstPageAsyncCache.TryGetValue(entityType, out var cachedDelegate))
            {
                cachedDelegate = CreateFirstPageDelegate(entityType, keyType);
                _firstPageAsyncCache.TryAdd(entityType, cachedDelegate);
            }

            if (cachedDelegate != null)
            {
                var task = (Task)cachedDelegate.DynamicInvoke(pageSize, ct)!;
                await task.ConfigureAwait(false);
                var result = GetTaskResult(task);
                return result is System.Collections.IEnumerable enumerable 
                    ? enumerable.Cast<object>() 
                    : Enumerable.Empty<object>();
            }

            // Fallback to reflection
            return await FirstPageAsyncReflection(entityType, keyType, pageSize, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BatchDataAccess] Error in FirstPageAsync for {EntityType}", entityType.Name);
            throw;
        }
    }

    // Private helper methods

    private static async Task<IEnumerable<T>> GetManyAsyncFallback<T, TKey>(IEnumerable<TKey> ids, CancellationToken ct)
        where T : class, Koan.Data.Abstractions.IEntity<TKey>
    {
        const int maxConcurrency = 10; // Limit concurrent database calls
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = ids.Select(async id =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await Data<T, TKey>.GetAsync(id, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.Where(r => r != null)!;
    }

    private static async Task UpsertManyAsyncFallback<T, TKey>(IEnumerable<T> entities, string? setName, CancellationToken ct)
        where T : class, Koan.Data.Abstractions.IEntity<TKey>
    {
        const int maxConcurrency = 10; // Limit concurrent database calls
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = entities.Select(async entity =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await Data<T, TKey>.UpsertAsync(entity, setName, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private static async Task DeleteManyAsyncFallback<T, TKey>(IEnumerable<TKey> ids, string? setName, CancellationToken ct)
        where T : class, Koan.Data.Abstractions.IEntity<TKey>
    {
        const int maxConcurrency = 10; // Limit concurrent database calls
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = ids.Select(async id =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                await Data<T, TKey>.DeleteAsync(id, setName, ct);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
    }

    private static Delegate? CreateGetManyDelegate(Type entityType, Type keyType)
    {
        try
        {
            var dataType = typeof(Data<,>).MakeGenericType(entityType, keyType);
            var method = dataType.GetMethod("GetManyAsync", BindingFlags.Public | BindingFlags.Static,
                new[] { typeof(IEnumerable<>).MakeGenericType(keyType), typeof(CancellationToken) });

            if (method == null) return null;

            var funcType = typeof(Func<,,>).MakeGenericType(
                typeof(IEnumerable<>).MakeGenericType(keyType),
                typeof(CancellationToken),
                typeof(Task<>).MakeGenericType(typeof(IEnumerable<>).MakeGenericType(entityType)));

            return Delegate.CreateDelegate(funcType, method);
        }
        catch
        {
            return null;
        }
    }

    private static Delegate? CreateUpsertManyDelegate(Type entityType, Type keyType)
    {
        try
        {
            var dataType = typeof(Data<,>).MakeGenericType(entityType, keyType);
            var method = dataType.GetMethod("UpsertManyAsync", BindingFlags.Public | BindingFlags.Static,
                new[] { typeof(IEnumerable<>).MakeGenericType(entityType), typeof(string), typeof(CancellationToken) });

            if (method == null) return null;

            var actionType = typeof(Func<,,,>).MakeGenericType(
                typeof(IEnumerable<>).MakeGenericType(entityType),
                typeof(string),
                typeof(CancellationToken),
                typeof(Task));

            return Delegate.CreateDelegate(actionType, method);
        }
        catch
        {
            return null;
        }
    }

    private static Delegate? CreateDeleteManyDelegate(Type entityType, Type keyType)
    {
        try
        {
            var dataType = typeof(Data<,>).MakeGenericType(entityType, keyType);
            var method = dataType.GetMethod("DeleteManyAsync", BindingFlags.Public | BindingFlags.Static,
                new[] { typeof(IEnumerable<>).MakeGenericType(keyType), typeof(string), typeof(CancellationToken) });

            if (method == null) return null;

            var actionType = typeof(Func<,,,>).MakeGenericType(
                typeof(IEnumerable<>).MakeGenericType(keyType),
                typeof(string),
                typeof(CancellationToken),
                typeof(Task));

            return Delegate.CreateDelegate(actionType, method);
        }
        catch
        {
            return null;
        }
    }

    private static Delegate? CreateFirstPageDelegate(Type entityType, Type keyType)
    {
        try
        {
            var dataType = typeof(Data<,>).MakeGenericType(entityType, keyType);
            var method = dataType.GetMethod("FirstPage", BindingFlags.Public | BindingFlags.Static,
                new[] { typeof(int), typeof(CancellationToken) });

            if (method == null) return null;

            var funcType = typeof(Func<,,>).MakeGenericType(
                typeof(int),
                typeof(CancellationToken),
                typeof(Task<>).MakeGenericType(typeof(IEnumerable<>).MakeGenericType(entityType)));

            return Delegate.CreateDelegate(funcType, method);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<object[]> GetManyAsyncReflection(Type entityType, Type keyType, List<object> ids, CancellationToken ct)
    {
        var dataType = typeof(Data<,>).MakeGenericType(entityType, keyType);
        var getManyMethod = dataType.GetMethod("GetManyAsync", BindingFlags.Public | BindingFlags.Static);

        if (getManyMethod != null)
        {
            var task = (Task)getManyMethod.Invoke(null, new object[] { ids, ct })!;
            await task.ConfigureAwait(false);
            var result = GetTaskResult(task);
            return result is System.Collections.IEnumerable enumerable 
                ? enumerable.Cast<object>().ToArray() 
                : Array.Empty<object>();
        }

        // Fallback to individual calls
        var getMethod = dataType.GetMethod("GetAsync", BindingFlags.Public | BindingFlags.Static,
            new[] { keyType, typeof(CancellationToken) });

        if (getMethod == null) return Array.Empty<object>();

        var results = new List<object>();
        foreach (var id in ids)
        {
            var task = (Task)getMethod.Invoke(null, new object[] { id, ct })!;
            await task.ConfigureAwait(false);
            var result = GetTaskResult(task);
            if (result != null) results.Add(result);
        }

        return results.ToArray();
    }

    private static async Task UpsertManyAsyncReflection(Type entityType, Type keyType, List<object> entities, string? setName, CancellationToken ct)
    {
        var dataType = typeof(Data<,>).MakeGenericType(entityType, keyType);
        var upsertManyMethod = dataType.GetMethod("UpsertManyAsync", BindingFlags.Public | BindingFlags.Static);

        if (upsertManyMethod != null)
        {
            var task = (Task)upsertManyMethod.Invoke(null, new object?[] { entities, setName, ct })!;
            await task.ConfigureAwait(false);
            return;
        }

        // Fallback to individual calls
        var upsertMethod = dataType.GetMethod("UpsertAsync", BindingFlags.Public | BindingFlags.Static,
            new[] { entityType, typeof(string), typeof(CancellationToken) });

        if (upsertMethod == null) return;

        foreach (var entity in entities)
        {
            var task = (Task)upsertMethod.Invoke(null, new object?[] { entity, setName, ct })!;
            await task.ConfigureAwait(false);
        }
    }

    private static async Task DeleteManyAsyncReflection(Type entityType, Type keyType, List<object> ids, string? setName, CancellationToken ct)
    {
        var dataType = typeof(Data<,>).MakeGenericType(entityType, keyType);
        var deleteManyMethod = dataType.GetMethod("DeleteManyAsync", BindingFlags.Public | BindingFlags.Static);

        if (deleteManyMethod != null)
        {
            var task = (Task)deleteManyMethod.Invoke(null, new object?[] { ids, setName, ct })!;
            await task.ConfigureAwait(false);
            return;
        }

        // Fallback to individual calls
        var deleteMethod = dataType.GetMethod("DeleteAsync", BindingFlags.Public | BindingFlags.Static,
            new[] { keyType, typeof(string), typeof(CancellationToken) });

        if (deleteMethod == null) return;

        foreach (var id in ids)
        {
            var task = (Task)deleteMethod.Invoke(null, new object?[] { id, setName, ct })!;
            await task.ConfigureAwait(false);
        }
    }

    private static async Task<IEnumerable<object>> FirstPageAsyncReflection(Type entityType, Type keyType, int pageSize, CancellationToken ct)
    {
        var dataType = typeof(Data<,>).MakeGenericType(entityType, keyType);
        var firstPageMethod = dataType.GetMethod("FirstPage", BindingFlags.Public | BindingFlags.Static,
            new[] { typeof(int), typeof(CancellationToken) });

        if (firstPageMethod == null) return Enumerable.Empty<object>();

        var task = (Task)firstPageMethod.Invoke(null, new object[] { pageSize, ct })!;
        await task.ConfigureAwait(false);
        var result = GetTaskResult(task);
        return result is System.Collections.IEnumerable enumerable 
            ? enumerable.Cast<object>() 
            : Enumerable.Empty<object>();
    }

    private static string GenerateCacheKey(Type entityType, Type keyType, string operation)
    {
        return $"{operation}_{entityType.FullName}_{keyType.FullName}";
    }

    private static object? GetTaskResult(Task task)
    {
        var type = task.GetType();
        if (type.IsGenericType)
        {
            return type.GetProperty("Result")?.GetValue(task);
        }
        return null;
    }
}