using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Sora.Flow.Infrastructure;
using Sora.Flow.Model;
using Sora.Flow.Options;
using Sora.Flow.Core.Data;
using Sora.Flow.Core.Services;
using Sora.Data.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Sora.Flow.Core.Services;

/// <summary>
/// Optimized version of ModelAssociationWorker with batch processing and parallel execution.
/// Replaces sequential key index lookups with bulk operations for significant performance improvement.
/// </summary>
public class OptimizedModelAssociationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<FlowOptions> _options;
    private readonly ILogger<OptimizedModelAssociationWorker> _logger;
    private readonly SemaphoreSlim _concurrencyControl;
    private readonly AdaptiveBatchProcessor _batchProcessor;

    public OptimizedModelAssociationWorker(
        IServiceProvider serviceProvider,
        IOptionsMonitor<FlowOptions> options,
        ILogger<OptimizedModelAssociationWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
        _concurrencyControl = new SemaphoreSlim(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
        _batchProcessor = new AdaptiveBatchProcessor();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[optimized-association] Starting optimized model association worker");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sw = Stopwatch.StartNew();
                var totalProcessed = 0;

                var models = DiscoverModels();
                if (models.Count > 0)
                {
                    var batchSize = await _batchProcessor.GetOptimalBatchSize();
                    
                    _logger.LogDebug("[optimized-association] Processing {ModelCount} models with batch size {BatchSize}", 
                        models.Count, batchSize);

                    // Process models in parallel with controlled concurrency
                    var modelTasks = models.Select(async modelType =>
                    {
                        await _concurrencyControl.WaitAsync(stoppingToken);
                        try
                        {
                            return await ProcessModelWithBatchOperations(modelType, batchSize, stoppingToken);
                        }
                        finally
                        {
                            _concurrencyControl.Release();
                        }
                    });

                    var results = await Task.WhenAll(modelTasks);
                    totalProcessed = results.Sum();

                    sw.Stop();
                    _batchProcessor.RecordMetrics(sw.Elapsed, totalProcessed, GC.GetTotalMemory(false));

                    if (totalProcessed > 0)
                    {
                        _logger.LogInformation("[optimized-association] Processed {Count} records in {Duration}ms", 
                            totalProcessed, sw.ElapsedMilliseconds);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("[optimized-association] Association worker cancellation requested");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[optimized-association] Error in association worker cycle");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        _logger.LogInformation("[optimized-association] Optimized association worker stopped");
    }

    private async Task<int> ProcessModelWithBatchOperations(Type modelType, int batchSize, CancellationToken ct)
    {
        try
        {
            var recordType = typeof(StageRecord<>).MakeGenericType(modelType);
            var kiType = typeof(KeyIndex<>).MakeGenericType(modelType);
            var setName = FlowSets.StageShort(FlowSets.Standardized);

            // Fetch records in batches using optimized data access
            var records = await BatchDataAccessHelper.FirstPageAsync(recordType, typeof(string), batchSize, ct);
            var recordList = records.ToList();

            if (recordList.Count == 0) return 0;

            _logger.LogDebug("[optimized-association] Processing {Count} {ModelType} records", 
                recordList.Count, modelType.Name);

            // Extract all candidates for batch processing
            var allCandidates = new List<(object record, string value)>();
            
            foreach (var record in recordList)
            {
                var candidates = ExtractCandidatesFromRecord(record, modelType);
                allCandidates.AddRange(candidates.Select(c => (record, c)));
            }

            if (allCandidates.Count == 0) return 0;

            // Batch resolve all key indexes in single database call
            var candidateValues = allCandidates.Select(c => c.value).Distinct().ToList();
            var keyIndexes = await BatchDataAccessHelper.GetManyAsync(kiType, typeof(string), 
                candidateValues.Cast<object>(), ct);

            // Create lookup dictionary for fast association
            var keyIndexLookup = keyIndexes.ToDictionary(
                ki => GetKeyIndexAggregationKey(ki),
                ki => GetKeyIndexReferenceUlid(ki),
                StringComparer.Ordinal);

            // Process associations in parallel
            var processedRecords = await ProcessAssociationsInParallel(
                allCandidates, keyIndexLookup, modelType, ct);

            // Batch transition successful records
            if (processedRecords.Count > 0)
            {
                await BatchTransitionRecords(processedRecords, modelType, 
                    FlowSets.StageShort(FlowSets.Standardized), 
                    FlowSets.StageShort(FlowSets.Keyed), ct);
            }

            return processedRecords.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[optimized-association] Error processing model {ModelType}", modelType.Name);
            return 0;
        }
    }

    private async Task<List<object>> ProcessAssociationsInParallel(
        List<(object record, string value)> candidates,
        Dictionary<string, string> keyIndexLookup,
        Type modelType,
        CancellationToken ct)
    {
        var processedRecords = new List<object>();
        var semaphore = new SemaphoreSlim(Environment.ProcessorCount, Environment.ProcessorCount);

        // Group candidates by record to avoid processing same record multiple times
        var recordGroups = candidates.GroupBy(c => c.record).ToList();

        var tasks = recordGroups.Select(async group =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                var record = group.Key;
                var recordCandidates = group.Select(g => g.value).ToList();

                // Find associations for this record
                var owners = new HashSet<string>(StringComparer.Ordinal);
                foreach (var candidate in recordCandidates)
                {
                    if (keyIndexLookup.TryGetValue(candidate, out var referenceUlid))
                    {
                        owners.Add(referenceUlid);
                    }
                }

                if (owners.Count > 0)
                {
                    // Update record with associations
                    var updatedRecord = CreateKeyedRecord(record, owners, modelType);
                    return updatedRecord;
                }

                return null;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        processedRecords.AddRange(results.Where(r => r != null)!);

        return processedRecords;
    }

    private async Task BatchTransitionRecords(List<object> records, Type modelType, string fromSet, string toSet, CancellationToken ct)
    {
        try
        {
            var keyedRecordType = typeof(KeyedRecord<>).MakeGenericType(modelType);
            
            // Batch upsert to destination
            await BatchDataAccessHelper.UpsertManyAsync(keyedRecordType, typeof(string), records, toSet, ct);

            // Batch delete from source  
            var recordIds = records.Select(GetRecordId).ToList();
            var stageRecordType = typeof(StageRecord<>).MakeGenericType(modelType);
            await BatchDataAccessHelper.DeleteManyAsync(stageRecordType, typeof(string), recordIds, fromSet, ct);

            _logger.LogDebug("[optimized-association] Batch transitioned {Count} {ModelType} records from {FromSet} to {ToSet}",
                records.Count, modelType.Name, fromSet, toSet);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[optimized-association] Error in batch transition for {ModelType}", modelType.Name);
            throw;
        }
    }

    private List<string> ExtractCandidatesFromRecord(object record, Type modelType)
    {
        var candidates = new List<string>();

        try
        {
            // Get data from StageRecord
            var data = record.GetType().GetProperty("Data")?.GetValue(record);
            if (data == null) return candidates;

            // Extract candidates based on model type configuration
            var modelInfo = FlowRegistry.GetEntityIdStructure(modelType);
            if (modelInfo.HasValue)
            {
                var keyComponents = modelInfo.Value.KeyComponents;
                foreach (var component in keyComponents)
                {
                    var value = ExtractValueFromData(data, component.Path);
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        candidates.Add(value);
                    }
                }
            }

            // Also check for common identifier patterns
            var commonPaths = new[] { "id", "Id", "identifier", "code", "key" };
            foreach (var path in commonPaths)
            {
                var value = ExtractValueFromData(data, path);
                if (!string.IsNullOrWhiteSpace(value) && !candidates.Contains(value))
                {
                    candidates.Add(value);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[optimized-association] Error extracting candidates from {ModelType} record", modelType.Name);
        }

        return candidates;
    }

    private string? ExtractValueFromData(object data, string path)
    {
        try
        {
            if (data is IDictionary<string, object?> dict)
            {
                return dict.TryGetValue(path, out var value) ? value?.ToString() : null;
            }

            var property = data.GetType().GetProperty(path, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return property?.GetValue(data)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private object CreateKeyedRecord(object stageRecord, HashSet<string> owners, Type modelType)
    {
        var keyedRecordType = typeof(KeyedRecord<>).MakeGenericType(modelType);
        var keyedRecord = Activator.CreateInstance(keyedRecordType)!;

        // Copy properties from stage record
        CopyRecordProperties(stageRecord, keyedRecord);

        // Set owners
        var ownersProperty = keyedRecordType.GetProperty("Owners");
        ownersProperty?.SetValue(keyedRecord, owners);

        return keyedRecord;
    }

    private void CopyRecordProperties(object source, object destination)
    {
        var sourceType = source.GetType();
        var destType = destination.GetType();

        var commonProperties = new[] { "Id", "SourceId", "Data", "Source", "OccurredAt" };
        
        foreach (var propName in commonProperties)
        {
            var sourceProp = sourceType.GetProperty(propName);
            var destProp = destType.GetProperty(propName);

            if (sourceProp != null && destProp != null && destProp.CanWrite)
            {
                var value = sourceProp.GetValue(source);
                destProp.SetValue(destination, value);
            }
        }
    }

    private string GetKeyIndexAggregationKey(object keyIndex)
    {
        return keyIndex.GetType().GetProperty("AggregationKey")?.GetValue(keyIndex) as string ?? "";
    }

    private string GetKeyIndexReferenceUlid(object keyIndex)
    {
        return keyIndex.GetType().GetProperty("ReferenceUlid")?.GetValue(keyIndex) as string ?? "";
    }

    private string GetRecordId(object record)
    {
        return record.GetType().GetProperty("Id")?.GetValue(record) as string ?? "";
    }

    private List<Type> DiscoverModels()
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

                if (def == typeof(FlowEntity<>) || def == typeof(DynamicFlowEntity<>))
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