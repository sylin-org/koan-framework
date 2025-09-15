using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Koan.Flow.Options;

namespace Koan.Flow.Materialization;

public interface IFlowMaterializer
{
    Task<(IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, string> policies)> MaterializeAsync(
        string modelName,
        IReadOnlyDictionary<string, IReadOnlyCollection<string?>> canonicalOrdered,
        CancellationToken ct);
}

internal sealed class FlowMaterializer : IFlowMaterializer
{
    private readonly IServiceProvider _sp;
    private readonly IOptionsMonitor<FlowMaterializationOptions> _opts;
    private readonly ILogger<FlowMaterializer> _log;

    private readonly ConcurrentDictionary<string, IRecordMaterializationTransformer?> _recordCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, IPropertyMaterializationTransformer> _policyCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _warnedRecord = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, bool> _warnedPolicy = new(StringComparer.OrdinalIgnoreCase);

    public FlowMaterializer(IServiceProvider sp, IOptionsMonitor<FlowMaterializationOptions> opts, ILogger<FlowMaterializer> log)
    { _sp = sp; _opts = opts; _log = log; }

    public async Task<(IReadOnlyDictionary<string, string?> values, IReadOnlyDictionary<string, string> policies)> MaterializeAsync(
        string modelName,
        IReadOnlyDictionary<string, IReadOnlyCollection<string?>> canonicalOrdered,
        CancellationToken ct)
    {
        // Record-level transformer override
        var record = GetRecordTransformer(modelName);
        if (record is not null)
        {
            WarnOnceRecord(modelName);
            var (vals, pols) = await record.MaterializeAsync(modelName, canonicalOrdered, ct).ConfigureAwait(false);
            return (vals, pols);
        }

        var opts = _opts.CurrentValue;
        var resultValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var resultPolicies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, values) in canonicalOrdered)
        {
            var policyName = ResolvePolicyName(opts, modelName, path);
            var transformer = GetPropertyTransformer(policyName);
            var decision = await transformer.MaterializeAsync(modelName, path, values, canonicalOrdered, ct).ConfigureAwait(false);
            resultValues[path] = decision.Value;
            resultPolicies[path] = string.IsNullOrWhiteSpace(decision.Policy) ? policyName : decision.Policy;
        }

        return (resultValues, resultPolicies);
    }

    private IRecordMaterializationTransformer? GetRecordTransformer(string modelName)
    {
        return _recordCache.GetOrAdd(modelName, name =>
        {
            var opts = _opts.CurrentValue;
            if (!opts.RecordTransformers.TryGetValue(modelName, out var typeName) || string.IsNullOrWhiteSpace(typeName))
                return null;
            var t = ResolveType(typeName);
            if (t is null)
            {
                WarnOncePolicy($"record:{modelName}", $"Koan.Flow materialization: record transformer type not found: {typeName} for model {modelName}");
                return null;
            }
            if (!typeof(IRecordMaterializationTransformer).IsAssignableFrom(t))
            {
                WarnOncePolicy($"record:{modelName}", $"Koan.Flow materialization: record transformer does not implement {nameof(IRecordMaterializationTransformer)}: {typeName}");
                return null;
            }
            return (IRecordMaterializationTransformer)ActivatorUtilities.CreateInstance(_sp, t);
        });
    }

    private string ResolvePolicyName(FlowMaterializationOptions opts, string modelName, string path)
    {
        if (opts.PerPath.TryGetValue($"{modelName}:{path}", out var perPath) && !string.IsNullOrWhiteSpace(perPath))
            return perPath.Trim();
        if (opts.PerModelDefaults.TryGetValue(modelName, out var perModel) && !string.IsNullOrWhiteSpace(perModel))
            return perModel.Trim();
        return string.IsNullOrWhiteSpace(opts.DefaultPolicy) ? BuiltInPolicies.Last : opts.DefaultPolicy.Trim();
    }

    private IPropertyMaterializationTransformer GetPropertyTransformer(string policyName)
    {
        return _policyCache.GetOrAdd(policyName, name =>
        {
            // Built-ins
            switch (name.Trim().ToLowerInvariant())
            {
                case BuiltInPolicies.Last: return new LastTransformer();
                case BuiltInPolicies.First: return new FirstTransformer();
                case BuiltInPolicies.Max: return new MaxTransformer();
                case BuiltInPolicies.Min: return new MinTransformer();
                case BuiltInPolicies.Coalesce: return new CoalesceTransformer();
            }

            // Custom via options
            var opts = _opts.CurrentValue;
            if (opts.PropertyTransformers.TryGetValue(name, out var typeName))
            {
                var t = ResolveType(typeName);
                if (t is not null && typeof(IPropertyMaterializationTransformer).IsAssignableFrom(t))
                {
                    return (IPropertyMaterializationTransformer)ActivatorUtilities.CreateInstance(_sp, t);
                }
            }

            WarnOncePolicy(name, $"Koan.Flow materialization: unknown policy '{name}', falling back to '{BuiltInPolicies.Last}'");
            return new LastTransformer();
        });
    }

    private static Type? ResolveType(string typeName)
    {
        // Try direct
        var t = Type.GetType(typeName, throwOnError: false, ignoreCase: false);
        if (t is not null) return t;
        // Try in loaded assemblies by FullName or Name
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                t = asm.GetType(typeName, throwOnError: false, ignoreCase: false);
                if (t is not null) return t;
                // match by FullName or Name
                foreach (var cand in asm.GetTypes())
                {
                    if (string.Equals(cand.FullName, typeName, StringComparison.Ordinal) || string.Equals(cand.Name, typeName, StringComparison.Ordinal))
                        return cand;
                }
            }
            catch (ReflectionTypeLoadException) { }
            catch { }
        }
        return null;
    }

    private void WarnOnceRecord(string modelName)
    {
        if (_warnedRecord.TryAdd(modelName, true))
        {
            _log.LogWarning("Koan.Flow materialization: record-level transformer configured for model {Model}. Property-level policies will be ignored for this model.", modelName);
        }
    }

    private void WarnOncePolicy(string key, string message)
    {
        if (_warnedPolicy.TryAdd(key, true))
        {
            _log.LogWarning(message);
        }
    }
}
