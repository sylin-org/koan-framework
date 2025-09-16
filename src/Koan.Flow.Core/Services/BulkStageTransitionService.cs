using Microsoft.Extensions.Logging;
using Koan.Flow.Core.Data;
using Koan.Flow.Infrastructure;
using Koan.Data.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Flow.Core.Services;

/// <summary>
/// Optimized stage transition service that moves records between Flow stages in bulk.
/// Replaces individual record movement with batch operations for significant performance improvement.
/// </summary>
public static class BulkStageTransitionService
{
    private static readonly ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    /// <summary>
    /// Transitions records between stages in bulk for maximum performance.
    /// </summary>
    public static async Task<int> TransitionRecordsBulk<T>(
        IEnumerable<T> records, 
        string fromStage, 
        string toStage, 
        CancellationToken ct = default) 
        where T : class, Koan.Data.Abstractions.IEntity<string>
    {
        var recordList = records.ToList();
        if (recordList.Count == 0) return 0;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("[bulk-transition] Starting bulk transition of {Count} {EntityType} records from {FromStage} to {ToStage}",
                recordList.Count, typeof(T).Name, fromStage, toStage);

            // Bulk upsert to destination stage
            await BatchDataAccessHelper.UpsertManyAsync<T, string>(recordList, toStage, ct);

            // Bulk delete from source stage
            var recordIds = recordList.Select(GetRecordId).Where(id => !string.IsNullOrEmpty(id)).ToList();
            if (recordIds.Count > 0)
            {
                await BatchDataAccessHelper.DeleteManyAsync<T, string>(recordIds, fromStage, ct);
            }

            stopwatch.Stop();
            LogTransitionMetrics(typeof(T), recordList.Count, fromStage, toStage, stopwatch.Elapsed);

            return recordList.Count;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogTransitionError(typeof(T), fromStage, toStage, ex);
            throw;
        }
    }

    /// <summary>
    /// Transitions multiple model types in parallel for maximum throughput.
    /// </summary>
    public static async Task<Dictionary<Type, int>> TransitionMultipleModelTypes(
        Dictionary<Type, (IEnumerable<object> records, string fromStage, string toStage)> transitions,
        CancellationToken ct = default)
    {
        var tasks = transitions.Select(async kv =>
        {
            var (modelType, (records, fromStage, toStage)) = kv;
            try
            {
                var count = await TransitionRecordsBulkDynamic(modelType, records, fromStage, toStage, ct);
                return new { ModelType = modelType, Count = count, Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[bulk-transition] Error transitioning {ModelType} records", modelType.Name);
                return new { ModelType = modelType, Count = 0, Success = false };
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(r => r.ModelType, r => r.Count);
    }

    /// <summary>
    /// Dynamic bulk transition for runtime-determined entity types.
    /// </summary>
    public static async Task<int> TransitionRecordsBulkDynamic(
        Type entityType,
        IEnumerable<object> records,
        string fromStage,
        string toStage,
        CancellationToken ct = default)
    {
        var recordList = records.ToList();
        if (recordList.Count == 0) return 0;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("[bulk-transition] Starting dynamic bulk transition of {Count} {EntityType} records from {FromStage} to {ToStage}",
                recordList.Count, entityType.Name, fromStage, toStage);

            // Bulk upsert to destination stage
            await BatchDataAccessHelper.UpsertManyAsync(entityType, typeof(string), recordList, toStage, ct);

            // Bulk delete from source stage
            var recordIds = recordList.Select(GetRecordIdDynamic).Where(id => !string.IsNullOrEmpty(id)).ToList();
            if (recordIds.Count > 0)
            {
                await BatchDataAccessHelper.DeleteManyAsync(entityType, typeof(string), recordIds.Cast<object>(), fromStage, ct);
            }

            stopwatch.Stop();
            LogTransitionMetrics(entityType, recordList.Count, fromStage, toStage, stopwatch.Elapsed);

            return recordList.Count;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            LogTransitionError(entityType, fromStage, toStage, ex);
            throw;
        }
    }

    /// <summary>
    /// Specialized transition for converting between different record types (e.g., StageRecord to KeyedRecord).
    /// </summary>
    public static async Task<int> TransitionWithTypeConversion<TSource, TTarget>(
        IEnumerable<TSource> sourceRecords,
        Func<TSource, TTarget> converter,
        string fromStage,
        string toStage,
        CancellationToken ct = default) 
        where TSource : class, Koan.Data.Abstractions.IEntity<string>
        where TTarget : class, Koan.Data.Abstractions.IEntity<string>
    {
        var sourceList = sourceRecords.ToList();
        if (sourceList.Count == 0) return 0;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("[bulk-transition] Starting type conversion transition of {Count} records from {SourceType} to {TargetType}",
                sourceList.Count, typeof(TSource).Name, typeof(TTarget).Name);

            // Convert all records
            var targetRecords = sourceList.Select(converter).ToList();

            // Bulk upsert converted records to destination
            await BatchDataAccessHelper.UpsertManyAsync<TTarget, string>(targetRecords, toStage, ct);

            // Bulk delete original records from source
            var recordIds = sourceList.Select(GetRecordId).Where(id => !string.IsNullOrEmpty(id)).ToList();
            if (recordIds.Count > 0)
            {
                await BatchDataAccessHelper.DeleteManyAsync<TSource, string>(recordIds, fromStage, ct);
            }

            stopwatch.Stop();
            _logger.LogDebug("[bulk-transition] Completed type conversion transition in {Duration}ms: {SourceType} -> {TargetType}",
                stopwatch.ElapsedMilliseconds, typeof(TSource).Name, typeof(TTarget).Name);

            return targetRecords.Count;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "[bulk-transition] Error in type conversion transition: {SourceType} -> {TargetType}", 
                typeof(TSource).Name, typeof(TTarget).Name);
            throw;
        }
    }

    /// <summary>
    /// Batch transition with filtering - only transitions records that meet the criteria.
    /// </summary>
    public static async Task<(int transitioned, int filtered)> TransitionWithFilter<T>(
        IEnumerable<T> records,
        Func<T, bool> filter,
        string fromStage,
        string toStage,
        CancellationToken ct = default) 
        where T : class, Koan.Data.Abstractions.IEntity<string>
    {
        var allRecords = records.ToList();
        if (allRecords.Count == 0) return (0, 0);

        var filteredRecords = allRecords.Where(filter).ToList();
        var filteredOutCount = allRecords.Count - filteredRecords.Count;

        if (filteredRecords.Count == 0)
        {
            _logger.LogDebug("[bulk-transition] All {Count} {EntityType} records filtered out", 
                allRecords.Count, typeof(T).Name);
            return (0, filteredOutCount);
        }

        var transitioned = await TransitionRecordsBulk(filteredRecords, fromStage, toStage, ct);
        
        _logger.LogDebug("[bulk-transition] Transitioned {Transitioned} of {Total} {EntityType} records (filtered out {Filtered})",
            transitioned, allRecords.Count, typeof(T).Name, filteredOutCount);

        return (transitioned, filteredOutCount);
    }

    /// <summary>
    /// Advanced bulk transition with custom validation and error handling.
    /// </summary>
    public static async Task<BulkTransitionResult<T>> TransitionWithValidation<T>(
        IEnumerable<T> records,
        Func<T, Task<bool>> validator,
        string fromStage,
        string toStage,
        CancellationToken ct = default) 
        where T : class, Koan.Data.Abstractions.IEntity<string>
    {
        var recordList = records.ToList();
        var result = new BulkTransitionResult<T>();

        if (recordList.Count == 0) return result;

        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("[bulk-transition] Starting validated bulk transition of {Count} {EntityType} records",
                recordList.Count, typeof(T).Name);

            // Validate records in parallel with concurrency control
            var semaphore = new SemaphoreSlim(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
            var validationTasks = recordList.Select(async record =>
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var isValid = await validator(record);
                    return new { Record = record, IsValid = isValid };
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var validationResults = await Task.WhenAll(validationTasks);
            
            result.ValidRecords = validationResults.Where(v => v.IsValid).Select(v => v.Record).ToList();
            result.InvalidRecords = validationResults.Where(v => !v.IsValid).Select(v => v.Record).ToList();

            // Transition only valid records
            if (result.ValidRecords.Count > 0)
            {
                result.TransitionedCount = await TransitionRecordsBulk(result.ValidRecords, fromStage, toStage, ct);
            }

            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;

            _logger.LogInformation("[bulk-transition] Validated transition completed: {Valid} valid, {Invalid} invalid, {Transitioned} transitioned in {Duration}ms",
                result.ValidRecords.Count, result.InvalidRecords.Count, result.TransitionedCount, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            result.ProcessingTime = stopwatch.Elapsed;
            result.Error = ex;
            _logger.LogError(ex, "[bulk-transition] Error in validated bulk transition for {EntityType}", typeof(T).Name);
            throw;
        }
    }

    /// <summary>
    /// Batch transition with automatic retry for failed records.
    /// </summary>
    public static async Task<int> TransitionWithRetry<T>(
        IEnumerable<T> records,
        string fromStage,
        string toStage,
        int maxRetries = 3,
        TimeSpan retryDelay = default,
        CancellationToken ct = default) 
        where T : class, Koan.Data.Abstractions.IEntity<string>
    {
        var recordList = records.ToList();
        if (recordList.Count == 0) return 0;

        var effectiveRetryDelay = retryDelay == default ? TimeSpan.FromSeconds(1) : retryDelay;
        var remainingRecords = recordList;
        var totalTransitioned = 0;

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                var transitioned = await TransitionRecordsBulk(remainingRecords, fromStage, toStage, ct);
                totalTransitioned += transitioned;
                
                _logger.LogDebug("[bulk-transition] Retry attempt {Attempt}: transitioned {Count} {EntityType} records",
                    attempt, transitioned, typeof(T).Name);
                
                break; // Success, exit retry loop
            }
            catch (Exception ex) when (attempt < maxRetries)
            {
                _logger.LogWarning(ex, "[bulk-transition] Attempt {Attempt} failed for {EntityType}, retrying in {Delay}ms", 
                    attempt, typeof(T).Name, effectiveRetryDelay.TotalMilliseconds);
                
                await Task.Delay(effectiveRetryDelay, ct);
                effectiveRetryDelay = TimeSpan.FromMilliseconds(effectiveRetryDelay.TotalMilliseconds * 2); // Exponential backoff
            }
        }

        return totalTransitioned;
    }

    // Helper methods

    private static string GetRecordId<T>(T record) 
        where T : class, Koan.Data.Abstractions.IEntity<string>
    {
        return record.GetType().GetProperty("Id")?.GetValue(record)?.ToString() ?? "";
    }

    private static string GetRecordIdDynamic(object record)
    {
        return record.GetType().GetProperty("Id")?.GetValue(record)?.ToString() ?? "";
    }

    private static void LogTransitionMetrics(Type entityType, int recordCount, string fromStage, string toStage, TimeSpan duration)
    {
        var throughput = recordCount / Math.Max(duration.TotalSeconds, 0.001);
        
        _logger.LogInformation("[bulk-transition] Bulk transitioned {Count} {EntityType} records from {FromStage} to {ToStage} in {Duration}ms (throughput: {Throughput:F2}/sec)",
            recordCount, entityType.Name, fromStage, toStage, duration.TotalMilliseconds, throughput);
    }

    private static void LogTransitionError(Type entityType, string fromStage, string toStage, Exception ex)
    {
        _logger.LogError(ex, "[bulk-transition] Failed to transition {EntityType} records from {FromStage} to {ToStage}",
            entityType.Name, fromStage, toStage);
    }
}

/// <summary>
/// Result object for bulk transition operations with validation.
/// </summary>
public class BulkTransitionResult<T> where T : class
{
    public List<T> ValidRecords { get; set; } = new();
    public List<T> InvalidRecords { get; set; } = new();
    public int TransitionedCount { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public Exception? Error { get; set; }

    public bool Success => Error == null && TransitionedCount > 0;
    public int TotalRecords => ValidRecords.Count + InvalidRecords.Count;
    public double SuccessRate => TotalRecords > 0 ? (double)ValidRecords.Count / TotalRecords : 0.0;
}