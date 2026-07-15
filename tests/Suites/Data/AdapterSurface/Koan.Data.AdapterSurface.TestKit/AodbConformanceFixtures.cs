using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Threading;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Model;

namespace Koan.Data.AdapterSurface.TestKit;

// The shared conformance fixtures (ARCH-0103 P5) — ONE discoverable Database-mode shard axis + the conformance
// entities, lifted into the TestKit so every adapter surface reuses them instead of re-declaring a per-project
// shard axis and scope. This assembly is referenced ONLY by the adapter connector test projects (never data-core / tenancy),
// so the discovered Database axis cannot perturb their byte-identity off-proofs. The axis is INERT without an ambient
// shard (its source-key provider returns null ⇒ the op falls through to the Default source).

/// <summary>The Container-mode conformance entity: written under different ambient partitions; each partition must
/// resolve to a distinct physical container (proven by <c>AodbConformanceSpecsBase</c>'s Container cell).</summary>
public sealed class ConformancePartitionDoc : Entity<ConformancePartitionDoc>
{
    public string Title { get; set; } = "";
    public bool Flag { get; set; }
    public byte ByteValue { get; set; }
    public sbyte SignedByteValue { get; set; }
    public short ShortValue { get; set; }
    public ushort UnsignedShortValue { get; set; }
    public int Sequence { get; set; }
}

/// <summary>The Database-mode conformance entity: a <see cref="ConformanceShardedAttribute"/> routes each op to the
/// data source named by the ambient shard (proven by the Database cell).</summary>
[ConformanceSharded]
public sealed class ConformanceShardedDoc : Entity<ConformanceShardedDoc>
{
    public string Title { get; set; } = "";
}

/// <summary>Marks an entity as routed by the conformance shard axis (Database mode).</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class ConformanceShardedAttribute : Attribute;

internal static class ConformanceShardMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> Cache = new();
    public static bool IsSharded(Type t)
        => Cache.GetOrAdd(t, static x => x.GetCustomAttribute<ConformanceShardedAttribute>(inherit: true) is not null);
}

/// <summary>A discoverable Database-mode axis: a <see cref="ConformanceShardedAttribute"/> entity routes each op to
/// the data source named by the ambient shard. Inert when no shard is in scope (the provider returns null ⇒
/// fall-through to Default).</summary>
public sealed class ConformanceShardAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("aodb-conformance-shard")
        .Mode(AxisMode.Database)
        .AppliesTo(ConformanceShardMetadata.IsSharded)
        .Field("shard", static () => ConformanceShardAmbient.Current, typeof(string));   // the per-operation SOURCE-KEY provider
}

/// <summary>The ambient shard scope — selects the data source a <see cref="ConformanceShardedAttribute"/> entity routes to.</summary>
public static class ConformanceShardAmbient
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
