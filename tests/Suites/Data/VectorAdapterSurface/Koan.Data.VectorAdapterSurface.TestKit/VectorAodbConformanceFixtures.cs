using System;
using System.Collections.Concurrent;
using System.Reflection;
using Koan.Data.Abstractions;
using Koan.Data.Core;
using Koan.Data.Core.Axes;
using Koan.Data.Core.Model;
using Koan.Tenancy;

namespace Koan.Data.VectorAdapterSurface.TestKit;

// The shared, discoverable conformance fixtures for VectorAodbConformanceSpecsBase (ARCH-0103 §6). They mirror the
// record-plane ConformanceShardAxis/Doc set in the AdapterSurface TestKit, but for the vector plane. The entities carry
// NO [VectorAdapter] — they route to whichever vector adapter the conformance host elects — so the ONE kit works against
// any single-adapter host (InMemory Docker-free today; an HTTP adapter could adopt it). These types live in the vector
// TestKit, referenced ONLY by the vector adapter test projects, so data-core/tenancy never discover the Database axis
// (off-proofs stay byte-identical), and the axis is inert without an ambient shard.

/// <summary>Tenant-scoped conformance entity (Shared / RowScoped) — non-[HostScoped], so tenancy applies.</summary>
public sealed class VectorConformanceTenantDoc : Entity<VectorConformanceTenantDoc> { }

/// <summary>Partition-isolated conformance entity (Container / ContainerScoped) — [HostScoped], tenancy-exempt.</summary>
[HostScoped]
public sealed class VectorConformancePartitionDoc : Entity<VectorConformancePartitionDoc> { }

/// <summary>Source-routed conformance entity (Database / DatabaseScoped) — [HostScoped], only the shard axis applies.</summary>
[VectorConformanceSharded]
[HostScoped]
public sealed class VectorConformanceShardedDoc : Entity<VectorConformanceShardedDoc> { }

[AttributeUsage(AttributeTargets.Class, Inherited = true, AllowMultiple = false)]
public sealed class VectorConformanceShardedAttribute : Attribute;

internal static class VectorConformanceShardMetadata
{
    private static readonly ConcurrentDictionary<Type, bool> Cache = new();
    public static bool IsSharded(Type t)
        => Cache.GetOrAdd(t, static x => x.GetCustomAttribute<VectorConformanceShardedAttribute>(inherit: true) is not null);
}

/// <summary>Ambient shard selector for the Database conformance cell.</summary>
public static class VectorConformanceShardAmbient
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

public sealed class VectorConformanceShardCarrier : IAmbientSliceCarrier
{
    public string AxisKey => "koan:vector-conformance-shard";
    public string? Capture() => VectorConformanceShardAmbient.Current is { } s ? "v1:" + s : null;
    public IDisposable Restore(string captured)
        => captured.StartsWith("v1:", StringComparison.Ordinal)
            ? VectorConformanceShardAmbient.Use(captured[3..])
            : throw new InvalidOperationException($"VectorConformanceShardCarrier cannot restore '{captured}'.");
    public IDisposable Suppress() => VectorConformanceShardAmbient.Use(null);
}

/// <summary>The Database-mode axis that folds the ambient shard into the routed source (and thence the vector name).</summary>
public sealed class VectorConformanceShardAxis : IDataAxis
{
    public void Declare(Axis axis) => axis
        .Named("vector-conformance-shard")
        .Mode(AxisMode.Database)
        .AppliesTo(VectorConformanceShardMetadata.IsSharded)
        .Field("shard", static () => VectorConformanceShardAmbient.Current, typeof(string))
        .Carries(new VectorConformanceShardCarrier());
}
