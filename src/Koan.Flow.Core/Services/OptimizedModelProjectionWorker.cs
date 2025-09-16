using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Koan.Flow.Infrastructure;
using Koan.Flow.Model;
using Koan.Flow.Options;
using Koan.Flow.Core.Data;
using Koan.Data.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Koan.Flow.Core.Services;

/// <summary>
/// High-performance ModelProjectionWorker with parallel processing and bulk operations.
/// Implements adaptive batching and stage transition optimization for maximum throughput.
/// </summary>
public class OptimizedModelProjectionWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<FlowOptions> _options;
    private readonly ILogger<OptimizedModelProjectionWorker> _logger;
    private readonly AdaptiveBatchProcessor _batchProcessor;
    private readonly SemaphoreSlim _concurrencyControl;

    public OptimizedModelProjectionWorker(
        IServiceProvider serviceProvider,
        IOptionsMonitor<FlowOptions> options,
        ILogger<OptimizedModelProjectionWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
        _batchProcessor = new AdaptiveBatchProcessor();
        _concurrencyControl = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[optimized-projection] Starting optimized projection worker");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var totalProcessed = 0;

                var taskTypes = DiscoverTaskTypes();
                if (taskTypes.Count > 0)
                {
                    var batchSize = await _batchProcessor.GetOptimalBatchSize();

                    _logger.LogDebug("[optimized-projection] Processing {TaskCount} task types with batch size {BatchSize}",
                        taskTypes.Count, batchSize);

                    // Process task types in parallel
                    var taskResults = await ProcessTaskTypesInParallel(taskTypes, batchSize, stoppingToken);
                    totalProcessed = taskResults.Sum();

                    sw.Stop();
                    _batchProcessor.RecordMetrics(sw.Elapsed, totalProcessed, GC.GetTotalMemory(false));

                    if (totalProcessed > 0)
                    {
                        _logger.LogInformation("[optimized-projection] Processed {Count} projections in {Duration}ms",
                            totalProcessed, sw.ElapsedMilliseconds);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("[optimized-projection] Projection worker cancellation requested");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[optimized-projection] Error in projection worker cycle");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        _logger.LogInformation("[optimized-projection] Optimized projection worker stopped");
    }

    private async Task<int[]> ProcessTaskTypesInParallel(List<Type> taskTypes, int batchSize, CancellationToken ct)
    {
        var tasks = taskTypes.Select(async taskType =>
        {
            await _concurrencyControl.WaitAsync(ct);
            try
            {
                return await ProcessTaskTypeWithBulkOperations(taskType, batchSize, ct);
            }
            finally
            {
                _concurrencyControl.Release();
            }
        });

        return await Task.WhenAll(tasks);
    }

    private async Task<int> ProcessTaskTypeWithBulkOperations(Type taskType, int batchSize, CancellationToken ct)
    {
        try
        {
            var sw = Stopwatch.StartNew();

            // Fetch records in batches using optimized data access
            var records = await BatchDataAccessHelper.FirstPageAsync(taskType, typeof(string), batchSize, ct);
            var recordList = records.ToList();

            if (recordList.Count == 0) return 0;

            _logger.LogDebug("[optimized-projection] Processing {Count} {TaskType} records",
                recordList.Count, taskType.Name);

            // Determine the model type for this task type
            var modelType = GetModelTypeFromTaskType(taskType);
            if (modelType == null)
            {
                _logger.LogWarning("[optimized-projection] Could not determine model type for {TaskType}", taskType.Name);
                return 0;
            }

            // Group records by ReferenceId for batch projection processing
            var recordsByReference = GroupRecordsByReference(recordList);

            // Process projections in parallel
            var projectionTasks = recordsByReference.Select(group =>
                ProcessReferenceProjection(group.Key, group.Value, modelType, ct));

            var projectionResults = await Task.WhenAll(projectionTasks);
            var successfulProjections = projectionResults.Where(r => r.success).ToList();

            // Batch create projections
            if (successfulProjections.Count > 0)
            {
                await BatchCreateProjections(successfulProjections, modelType, ct);

                // Batch transition processed records
                var processedRecords = successfulProjections.SelectMany(r => r.records).ToList();
                await BatchTransitionRecords(processedRecords, taskType, ct);
            }

            sw.Stop();
            _logger.LogDebug("[optimized-projection] Completed {TaskType} processing in {Duration}ms",
                taskType.Name, sw.ElapsedMilliseconds);

            return successfulProjections.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[optimized-projection] Error processing task type {TaskType}", taskType.Name);
            return 0;
        }
    }

    private Dictionary<string, List<object>> GroupRecordsByReference(List<object> records)
    {
        var groups = new Dictionary<string, List<object>>(StringComparer.Ordinal);

        foreach (var record in records)
        {
            var ReferenceId = GetRecordReferenceId(record);
            if (!string.IsNullOrWhiteSpace(ReferenceId))
            {
                if (!groups.TryGetValue(ReferenceId, out var group))
                {
                    group = new List<object>();
                    groups[ReferenceId] = group;
                }
                group.Add(record);
            }
        }

        return groups;
    }

    private async Task<(bool success, List<object> records, object? canonicalProjection, object? lineageProjection)>
        ProcessReferenceProjection(string ReferenceId, List<object> records, Type modelType, CancellationToken ct)
    {
        try
        {
            // Create canonical projection by merging all records for this reference
            var canonicalProjection = await CreateCanonicalProjection(ReferenceId, records, modelType, ct);

            // Create lineage projection with full history
            var lineageProjection = await CreateLineageProjection(ReferenceId, records, modelType, ct);

            return (true, records, canonicalProjection, lineageProjection);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[optimized-projection] Error processing projection for reference {ReferenceId}", ReferenceId);
            return (false, records, null, null);
        }
    }

    private async Task<object> CreateCanonicalProjection(string ReferenceId, List<object> records, Type modelType, CancellationToken ct)
    {
        var canonicalType = typeof(CanonicalProjection<>).MakeGenericType(modelType);
        var canonical = Activator.CreateInstance(canonicalType)!;

        // Set basic properties
        canonicalType.GetProperty("ReferenceId")?.SetValue(canonical, ReferenceId);
        canonicalType.GetProperty("Id")?.SetValue(canonical, ReferenceId);
        canonicalType.GetProperty("LastUpdated")?.SetValue(canonical, DateTimeOffset.UtcNow);

        // Merge data from all records
        var mergedData = MergeRecordData(records, modelType);
        canonicalType.GetProperty("Data")?.SetValue(canonical, mergedData);

        // Set record count
        canonicalType.GetProperty("RecordCount")?.SetValue(canonical, records.Count);

        return canonical;
    }

    private async Task<object> CreateLineageProjection(string ReferenceId, List<object> records, Type modelType, CancellationToken ct)
    {
        var lineageType = typeof(LineageProjection<>).MakeGenericType(modelType);
        var lineage = Activator.CreateInstance(lineageType)!;

        // Set basic properties
        lineageType.GetProperty("ReferenceId")?.SetValue(lineage, ReferenceId);
        lineageType.GetProperty("Id")?.SetValue(lineage, ReferenceId);
        lineageType.GetProperty("LastUpdated")?.SetValue(lineage, DateTimeOffset.UtcNow);

        // Create lineage entries
        var lineageEntries = records.Select(record => CreateLineageEntry(record, modelType)).ToList();
        lineageType.GetProperty("Lineage")?.SetValue(lineage, lineageEntries);

        return lineage;
    }

    private object MergeRecordData(List<object> records, Type modelType)
    {
        var mergedData = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        // Process records in chronological order (most recent wins for conflicts)
        var sortedRecords = records.OrderBy(GetRecordOccurredAt).ToList();

        foreach (var record in sortedRecords)
        {
            var data = GetRecordData(record);
            if (data is IDictionary<string, object?> dataDict)
            {
                foreach (var kv in dataDict)
                {
                    mergedData[kv.Key] = kv.Value;
                }
            }
        }

        return mergedData;
    }

    private object CreateLineageEntry(object record, Type modelType)
    {
        return new
        {
            SourceId = GetRecordSourceId(record),
            Data = GetRecordData(record),
            Source = GetRecordSource(record),
            OccurredAt = GetRecordOccurredAt(record)
        };
    }

    private async Task BatchCreateProjections(
        List<(bool success, List<object> records, object? canonicalProjection, object? lineageProjection)> projections,
        Type modelType,
        CancellationToken ct)
    {
        try
        {
            // Batch create canonical projections
            var canonicalProjections = projections
                .Where(p => p.canonicalProjection != null)
                .Select(p => p.canonicalProjection!)
                .ToList();

            if (canonicalProjections.Count > 0)
            {
                var canonicalType = typeof(CanonicalProjection<>).MakeGenericType(modelType);
                await BatchDataAccessHelper.UpsertManyAsync(canonicalType, typeof(string), canonicalProjections,
                    FlowSets.ViewShort(Constants.Views.Canonical), ct);

                _logger.LogDebug("[optimized-projection] Batch created {Count} canonical projections for {ModelType}",
                    canonicalProjections.Count, modelType.Name);
            }

            // Batch create lineage projections
            var lineageProjections = projections
                .Where(p => p.lineageProjection != null)
                .Select(p => p.lineageProjection!)
                .ToList();

            if (lineageProjections.Count > 0)
            {
                var lineageType = typeof(LineageProjection<>).MakeGenericType(modelType);
                await BatchDataAccessHelper.UpsertManyAsync(lineageType, typeof(string), lineageProjections,
                    FlowSets.ViewShort(Constants.Views.Lineage), ct);

                _logger.LogDebug("[optimized-projection] Batch created {Count} lineage projections for {ModelType}",
                    lineageProjections.Count, modelType.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[optimized-projection] Error in batch projection creation for {ModelType}", modelType.Name);
            throw;
        }
    }

    private async Task BatchTransitionRecords(List<object> records, Type taskType, CancellationToken ct)
    {
        try
        {
            var recordIds = records.Select(GetRecordId).ToList();

            // Batch delete from keyed stage
            await BatchDataAccessHelper.DeleteManyAsync(taskType, typeof(string), recordIds,
                FlowSets.StageShort(FlowSets.Keyed), ct);

            _logger.LogDebug("[optimized-projection] Batch transitioned {Count} {TaskType} records",
                records.Count, taskType.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[optimized-projection] Error in batch record transition for {TaskType}", taskType.Name);
            throw;
        }
    }

    // Helper methods for record property extraction

    private string GetRecordReferenceId(object record)
    {
        return GetRecordProperty(record, "ReferenceId") ?? "";
    }

    private string GetRecordId(object record)
    {
        return GetRecordProperty(record, "Id") ?? "";
    }

    private string GetRecordSourceId(object record)
    {
        return GetRecordProperty(record, "SourceId") ?? "";
    }

    private object? GetRecordData(object record)
    {
        return record.GetType().GetProperty("Data")?.GetValue(record);
    }

    private object? GetRecordSource(object record)
    {
        return record.GetType().GetProperty("Source")?.GetValue(record);
    }

    private DateTimeOffset GetRecordOccurredAt(object record)
    {
        var value = record.GetType().GetProperty("OccurredAt")?.GetValue(record);
        return value is DateTimeOffset dto ? dto : DateTimeOffset.UtcNow;
    }

    private string? GetRecordProperty(object record, string propertyName)
    {
        return record.GetType().GetProperty(propertyName)?.GetValue(record)?.ToString();
    }

    private Type? GetModelTypeFromTaskType(Type taskType)
    {
        try
        {
            // Extract model type from KeyedRecord<TModel> generic parameter
            if (taskType.IsGenericType)
            {
                var genericTypeDef = taskType.GetGenericTypeDefinition();
                if (genericTypeDef == typeof(KeyedRecord<>))
                {
                    return taskType.GetGenericArguments()[0];
                }
            }

            // Fallback: try to match by naming convention
            var typeName = taskType.Name;
            if (typeName.StartsWith("KeyedRecord"))
            {
                // Try to resolve model type from type name
                var modelName = typeName.Replace("KeyedRecord", "").Replace("`1", "");
                return FlowRegistry.ResolveModel(modelName);
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[optimized-projection] Error determining model type for {TaskType}", taskType.Name);
            return null;
        }
    }

    private List<Type> DiscoverTaskTypes()
    {
        var result = new List<Type>();
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();

        foreach (var asm in assemblies)
        {
            Type?[] types;
            try { types = asm.GetTypes(); }
            catch (ReflectionTypeLoadException rtle) { types = rtle.Types; }
            catch { continue; }

            foreach (var t in types)
            {
                if (t is null || !t.IsClass || t.IsAbstract) continue;
                var bt = t.BaseType;
                if (bt is null || !bt.IsGenericType) continue;
                var def = bt.GetGenericTypeDefinition();

                // Look for KeyedRecord<> types (projection task types)
                if (def == typeof(KeyedRecord<>))
                {
                    result.Add(t);
                }
            }
        }

        return result;
    }

    public override void Dispose()
    {
        _concurrencyControl?.Dispose();
        base.Dispose();
    }
}