using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Koan.Flow.Materialization;

public static class BuiltInPolicies
{
    public const string Last = "last";
    public const string First = "first";
    public const string Max = "max";
    public const string Min = "min";
    public const string Coalesce = "coalesce";
}

internal sealed class LastTransformer : IPropertyMaterializationTransformer
{
    public Task<MaterializedDecision> MaterializeAsync(string modelName, string path, IReadOnlyCollection<string?> values, IReadOnlyDictionary<string, IReadOnlyCollection<string?>> canonical, CancellationToken ct)
    {
        var v = values.LastOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return Task.FromResult(new MaterializedDecision(v, BuiltInPolicies.Last));
    }
}

internal sealed class FirstTransformer : IPropertyMaterializationTransformer
{
    public Task<MaterializedDecision> MaterializeAsync(string modelName, string path, IReadOnlyCollection<string?> values, IReadOnlyDictionary<string, IReadOnlyCollection<string?>> canonical, CancellationToken ct)
    {
        var v = values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return Task.FromResult(new MaterializedDecision(v, BuiltInPolicies.First));
    }
}

internal sealed class MaxTransformer : IPropertyMaterializationTransformer
{
    public Task<MaterializedDecision> MaterializeAsync(string modelName, string path, IReadOnlyCollection<string?> values, IReadOnlyDictionary<string, IReadOnlyCollection<string?>> canonical, CancellationToken ct)
    {
        double? best = null; string? raw = null;
        foreach (var s in values)
        {
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                if (best is null || d > best)
                { best = d; raw = d.ToString(CultureInfo.InvariantCulture); }
            }
        }
        return Task.FromResult(new MaterializedDecision(raw, BuiltInPolicies.Max));
    }
}

internal sealed class MinTransformer : IPropertyMaterializationTransformer
{
    public Task<MaterializedDecision> MaterializeAsync(string modelName, string path, IReadOnlyCollection<string?> values, IReadOnlyDictionary<string, IReadOnlyCollection<string?>> canonical, CancellationToken ct)
    {
        double? best = null; string? raw = null;
        foreach (var s in values)
        {
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                if (best is null || d < best)
                { best = d; raw = d.ToString(CultureInfo.InvariantCulture); }
            }
        }
        return Task.FromResult(new MaterializedDecision(raw, BuiltInPolicies.Min));
    }
}

internal sealed class CoalesceTransformer : IPropertyMaterializationTransformer
{
    public Task<MaterializedDecision> MaterializeAsync(string modelName, string path, IReadOnlyCollection<string?> values, IReadOnlyDictionary<string, IReadOnlyCollection<string?>> canonical, CancellationToken ct)
    {
        var v = values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
        return Task.FromResult(new MaterializedDecision(v, BuiltInPolicies.Coalesce));
    }
}
