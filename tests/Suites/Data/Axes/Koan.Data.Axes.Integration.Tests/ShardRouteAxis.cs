using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using Koan.Data.Core.Axes;

namespace Koan.Data.Axes.Integration.Tests;

/// <summary>
/// A discoverable <b>Database-mode</b> axis (ARCH-0102 §3) for the auto-routing proof: a <c>[Sharded]</c> entity routes
/// each operation to the data source named by the ambient shard (<see cref="ShardAmbient.Current"/>). The <c>.Field</c>
/// value provider is the per-operation SOURCE-KEY provider. No managed column, no read-filter — a separate data source
/// IS the isolation. Inert when no shard is in scope (the provider returns <c>null</c> ⇒ <c>AdapterResolver</c> falls
/// through to its normal chain).
/// </summary>
public sealed class ShardRouteAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("shard-route")
        .Mode(AxisMode.Database)
        .AppliesTo(ShardMetadata.IsSharded)
        .Field("shard", static () => ShardAmbient.Current, typeof(string));   // the per-operation SOURCE-KEY provider
}

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class ShardedAttribute : Attribute;

internal static class ShardMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> Cache = new();
    public static bool IsSharded(Type t)
        => Cache.GetOrAdd(t, static x => x.GetCustomAttribute<ShardedAttribute>(inherit: true) is not null);
}

/// <summary>The ambient shard scope — selects the data source a <c>[Sharded]</c> entity routes to (the tenant-scope analogue).</summary>
public static class ShardAmbient
{
    private static readonly AsyncLocal<string?> _shard = new();
    public static string? Current => _shard.Value;
    public static IDisposable Use(string? shard)
    {
        var prev = _shard.Value;
        _shard.Value = shard;
        return new Scope(prev);
    }
    private sealed class Scope(string? previous) : IDisposable
    {
        private bool _done;
        public void Dispose() { if (_done) return; _done = true; _shard.Value = previous; }
    }
}
