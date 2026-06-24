using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using Koan.Data.Core.Axes;

namespace Koan.Data.Axes.Integration.Tests;

/// <summary>
/// A discoverable <b>equality</b> axis (the tenant analogue) for the D5 multi-axis composition proof: a
/// <c>[Regional]</c> entity carries an invisible <c>__region</c> managed field stamped from the ambient region and
/// AND-folded into reads as an equality (auto-derived by the built-in contributor — no <c>.Reads</c>). Paired with the
/// predicate <see cref="ArchivedAxis"/> on one entity, it proves two expander-produced axes fold into one read together.
/// </summary>
public sealed class RegionAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("region")
        .AppliesTo(RegionMetadata.IsRegional)
        .Field("__region", static () => RegionAmbient.Current);   // equality: AutoReadFilter stays on (no .Reads)
}

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class RegionalAttribute : Attribute;

internal static class RegionMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> Cache = new();
    public static bool IsRegional(Type t)
        => Cache.GetOrAdd(t, static x => x.GetCustomAttribute<RegionalAttribute>(inherit: true) is not null);
}

/// <summary>The ambient region scope (the tenant-scope analogue).</summary>
public static class RegionAmbient
{
    private static readonly AsyncLocal<string?> _region = new();
    public static string? Current => _region.Value;
    public static IDisposable Use(string region)
    {
        var prev = _region.Value;
        _region.Value = region;
        return new Scope(prev);
    }
    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _done;
        public void Dispose() { if (_done) return; _done = true; _region.Value = previous; }
    }
}
