using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;
using Koan.Canon.Actions;
using Koan.Canon.Model;
using Koan.Canon.Infrastructure;
using Koan.Canon.Options;
using Koan.Canon.Core.Extensions;
using Koan.Data.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Canon.Core.Services;

/// <summary>
/// Background service that monitors Koan.Canon's native parked collection 
/// and resolves parent keys for records that were parked with "PARENT_NOT_FOUND" status.
/// Implements batch processing for improved performance and service poke pattern for immediate triggering.
/// </summary>
public class ParentKeyResolutionService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptionsMonitor<CanonOptions> _options;
    private readonly ILogger<ParentKeyResolutionService> _logger;
    private readonly SemaphoreSlim _resolutionLock = new(1, 1);
    private readonly ICanonActions _CanonActions;

    public ParentKeyResolutionService(
        IServiceProvider serviceProvider,
        IOptionsMonitor<CanonOptions> options,
        ILogger<ParentKeyResolutionService> logger,
        ICanonActions CanonActions)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
        _CanonActions = CanonActions;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[Canon.parentkey] ParentKeyResolutionService started");

        // Initial resolution on startup
        await ProcessPendingResolutions(stoppingToken);

        // Periodic processing every minute
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                await ProcessPendingResolutions(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("[Canon.parentkey] ParentKeyResolutionService cancellation requested");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Canon.parentkey] Error in parent key resolution cycle");

                // Wait before retrying on error
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }

        _logger.LogInformation("[Canon.parentkey] ParentKeyResolutionService stopped");
    }

    /// <summary>
    /// Service poke method for immediate triggering of parent key resolution.
    /// Called when new PARENT_NOT_FOUND records are created.
    /// </summary>
    public async Task TriggerResolutionAsync()
    {
        if (_resolutionLock.Wait(0)) // Non-blocking check
        {
            try
            {
                _logger.LogDebug("[Canon.parentkey] Triggered immediate parent key resolution");
                await ProcessPendingResolutions(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[Canon.parentkey] Error in triggered parent key resolution");
            }
            finally
            {
                _resolutionLock.Release();
            }
        }
        else
        {
            _logger.LogDebug("[Canon.parentkey] Resolution already in progress, skipping trigger");
        }
    }

    private async Task ProcessPendingResolutions(CancellationToken ct)
    {
        await _resolutionLock.WaitAsync(ct);
        try
        {
            var models = DiscoverModelsWithParentKeys();
            var totalResolved = 0;

            _logger.LogDebug("[Canon.parentkey] Starting resolution cycle for {ModelCount} models with parent keys", models.Count);

            // Process cycles until no more resolutions are possible
            do
            {
                var cycleResolved = 0;

                foreach (var modelType in models)
                {
                    try
                    {
                        var resolved = await ProcessModelParentKeys(modelType, ct);
                        cycleResolved += resolved;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "[Canon.parentkey] Error processing parent keys for model {ModelType}", modelType.Name);
                    }
                }

                totalResolved += cycleResolved;

                if (cycleResolved > 0)
                {
                    _logger.LogInformation("[Canon.parentkey] Cycle resolved {Count} parent key records", cycleResolved);
                }

                // Continue until no more resolutions in this cycle (cascading resolution)
            } while (totalResolved > 0 && !ct.IsCancellationRequested);

            if (totalResolved > 0)
            {
                _logger.LogInformation("[Canon.parentkey] Total resolved {Count} parent key records", totalResolved);
            }
            else
            {
                _logger.LogTrace("[Canon.parentkey] No parent key records to resolve");
            }
        }
        finally
        {
            _resolutionLock.Release();
        }
    }

    private async Task<int> ProcessModelParentKeys(Type modelType, CancellationToken ct)
    {
        var parkedRecords = await GetParkedRecordsWithParentNotFound(modelType, ct);
        if (parkedRecords.Count == 0)
        {
            return 0;
        }

        _logger.LogDebug("[Canon.parentkey] Processing {Count} parked records for model {ModelType}",
            parkedRecords.Count, modelType.Name);

        // Batch resolve all parent keys for this model
        var resolutionMap = await BatchResolveParentKeys(modelType, parkedRecords, ct);

        // Process resolved records using Canon's healing pattern
        var resolvedCount = 0;
        foreach (var parkedRecord in parkedRecords)
        {
            try
            {
                var parentKey = ExtractParentKeyFromEvidence(parkedRecord);
                var sourceSystem = ExtractSourceSystemFromEvidence(parkedRecord);
                var resolutionKey = $"{sourceSystem}|{parentKey}";

                if (resolutionMap.TryGetValue(resolutionKey, out var resolvedParentUlid))
                {
                    // Update the record's parent key with resolved ULID
                    var healedData = await UpdateParentKeyInRecord(parkedRecord, resolvedParentUlid, ct);
                    if (healedData != null)
                    {
                        // Use the ParkedRecordExtensions.HealAsync method via reflection
                        var modelDataType = healedData.GetType();
                        var parkedRecordType = typeof(ParkedRecord<>).MakeGenericType(modelDataType);
                        var extensionMethod = typeof(ParkedRecordExtensions).GetMethod("HealAsync",
                            new[] { parkedRecordType, typeof(ICanonActions), modelDataType, typeof(string), typeof(string), typeof(CancellationToken) });

                        if (extensionMethod != null)
                        {
                            var healTask = (Task)extensionMethod.Invoke(null,
                                new object?[] { parkedRecord, _CanonActions, healedData, $"Parent key resolved to {resolvedParentUlid}", null, ct })!;
                            await healTask.ConfigureAwait(false);

                            resolvedCount++;
                            var recordId = parkedRecord.GetType().GetProperty("Id")?.GetValue(parkedRecord);
                            _logger.LogDebug("[Canon.parentkey] Healed parked record {Id} with resolved parent {ParentUlid}",
                                recordId, resolvedParentUlid);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                var recordId = parkedRecord.GetType().GetProperty("Id")?.GetValue(parkedRecord);
                _logger.LogError(ex, "[Canon.parentkey] Failed to heal parked record {Id}", recordId);
            }
        }

        return resolvedCount;
    }

    private async Task<List<object>> GetParkedRecordsWithParentNotFound(Type modelType, CancellationToken ct)
    {
        var parkedRecordType = typeof(ParkedRecord<>).MakeGenericType(modelType);
        var dataType = typeof(Data<,>).MakeGenericType(parkedRecordType, typeof(string));

        using var context = DataSetContext.With(CanonSets.StageShort(CanonSets.Parked));

        var pageMethod = dataType.GetMethod("FirstPage", BindingFlags.Public | BindingFlags.Static,
            new[] { typeof(int), typeof(CancellationToken) });

        if (pageMethod == null) return new List<object>();

        var task = (Task)pageMethod.Invoke(null, new object[] { 500, ct })!;
        await task.ConfigureAwait(false);
        var allParkedRecords = (System.Collections.IEnumerable)GetTaskResult(task)!;

        return allParkedRecords.Cast<object>()
            .Where(pr => string.Equals(pr.GetType().GetProperty("ReasonCode")?.GetValue(pr) as string,
                "PARENT_NOT_FOUND", StringComparison.Ordinal))
            .ToList();
    }

    private async Task<Dictionary<string, string>> BatchResolveParentKeys(
        Type modelType,
        List<object> parkedRecords,
        CancellationToken ct)
    {
        var resolutionMap = new Dictionary<string, string>();
        var parentInfo = CanonRegistry.GetEntityParent(modelType);

        if (!parentInfo.HasValue) return resolutionMap;

        // Group parent keys by source system for batch lookup
        var parentKeyGroups = parkedRecords
            .Select(rec => new
            {
                Record = rec,
                SourceSystem = ExtractSourceSystemFromEvidence(rec),
                ParentKey = ExtractParentKeyFromEvidence(rec)
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.SourceSystem) && !string.IsNullOrWhiteSpace(x.ParentKey))
            .GroupBy(x => x.SourceSystem)
            .ToList();

        foreach (var group in parentKeyGroups)
        {
            var sourceSystem = group.Key;
            var parentKeys = group.Select(x => x.ParentKey).Distinct().ToList();

            if (parentKeys.Count == 0) continue;

            _logger.LogDebug("[Canon.parentkey] Batch resolving {Count} parent keys for source system {SourceSystem}",
                parentKeys.Count, sourceSystem);

            // Batch lookup all parent keys for this source system
            var resolved = await BatchLookupIdentityLinks(
                parentInfo.Value.Parent,
                sourceSystem,
                parentKeys,
                ct);

            foreach (var kv in resolved)
            {
                var resolutionKey = $"{sourceSystem}|{kv.Key}";
                resolutionMap[resolutionKey] = kv.Value;
            }
        }

        return resolutionMap;
    }

    private async Task<Dictionary<string, string>> BatchLookupIdentityLinks(
        Type parentType,
        string sourceSystem,
        List<string> parentKeys,
        CancellationToken ct)
    {
        var resolved = new Dictionary<string, string>();
        var identityLinkType = typeof(IdentityLink<>).MakeGenericType(parentType);
        var dataType = typeof(Data<,>).MakeGenericType(identityLinkType, typeof(string));
        var getMethod = dataType.GetMethod("GetAsync", BindingFlags.Public | BindingFlags.Static,
            new[] { typeof(string), typeof(CancellationToken) });

        if (getMethod == null) return resolved;

        // Batch lookup each parent key
        var lookupTasks = parentKeys.Select(async parentKey =>
        {
            var composite = string.Join('|', sourceSystem, sourceSystem, parentKey);
            var task = (Task)getMethod.Invoke(null, new object[] { composite, ct })!;
            await task.ConfigureAwait(false);
            var link = GetTaskResult(task);

            if (link != null)
            {
                var refUlid = identityLinkType.GetProperty("ReferenceId")?.GetValue(link) as string;
                if (!string.IsNullOrWhiteSpace(refUlid))
                {
                    return new KeyValuePair<string, string>(parentKey, refUlid);
                }
            }

            return (KeyValuePair<string, string>?)null;
        });

        var results = await Task.WhenAll(lookupTasks);

        foreach (var result in results.Where(r => r.HasValue))
        {
            resolved[result.Value.Key] = result.Value.Value;
        }

        return resolved;
    }

    private string? ExtractParentKeyFromEvidence(object parkedRecord)
    {
        var evidence = parkedRecord.GetType().GetProperty("Evidence")?.GetValue(parkedRecord);
        if (evidence == null) return null;

        // Evidence contains the parentKey field from the SaveRejectAndDrop call
        var parentKeyProp = evidence.GetType().GetProperty("parentKey");
        return parentKeyProp?.GetValue(evidence) as string;
    }

    private string? ExtractSourceSystemFromEvidence(object parkedRecord)
    {
        var evidence = parkedRecord.GetType().GetProperty("Evidence")?.GetValue(parkedRecord);
        if (evidence == null) return null;

        // Evidence contains the source field from the SaveRejectAndDrop call  
        var sourceProp = evidence.GetType().GetProperty("source");
        return sourceProp?.GetValue(evidence) as string;
    }

    private async Task<object?> UpdateParentKeyInRecord(object parkedRecord, string resolvedParentUlid, CancellationToken ct)
    {
        var data = parkedRecord.GetType().GetProperty("Data")?.GetValue(parkedRecord);
        if (data == null) return null;

        var modelType = data.GetType();

        // Find the parent key property for this model
        var parentInfo = CanonRegistry.GetEntityParent(modelType);
        if (!parentInfo.HasValue) return null;

        var parentKeyPath = parentInfo.Value.ParentKeyPath;

        // Create a copy of the data and update the parent key
        var healedData = CopyModel(data);

        // Update the parent key field with the resolved ULID
        var parentKeyProp = modelType.GetProperty(parentKeyPath,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (parentKeyProp != null && parentKeyProp.CanWrite)
        {
            parentKeyProp.SetValue(healedData, resolvedParentUlid);
            return healedData;
        }

        return null;
    }

    private List<Type> DiscoverModelsWithParentKeys()
    {
        var result = new List<Type>();
        // Use cached assemblies instead of bespoke AppDomain scanning
        var assemblies = Koan.Core.Hosting.Bootstrap.AssemblyCache.Instance.GetAllAssemblies();

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

                // Check for CanonEntity<> or DynamicCanonEntity<> with parent relationships
                if ((def == typeof(CanonEntity<>) || def == typeof(DynamicCanonEntity<>)) &&
                    (CanonRegistry.GetEntityParent(t).HasValue || CanonRegistry.GetValueObjectParent(t).HasValue))
                {
                    result.Add(t);
                }
            }
        }

        return result;
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

    private static object CopyModel(object original)
    {
        if (original == null) return new object();

        // Use JSON serialization for deep copy
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(original, Koan.Core.Json.JsonDefaults.Settings);
        return Newtonsoft.Json.JsonConvert.DeserializeObject(json, original.GetType(), Koan.Core.Json.JsonDefaults.Settings) ?? new object();
    }

    public override void Dispose()
    {
        _resolutionLock?.Dispose();
        base.Dispose();
    }
}


