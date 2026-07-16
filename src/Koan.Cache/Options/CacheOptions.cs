using System;
using System.Collections.Generic;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Options;

public sealed class CacheOptions
{
    private static readonly string[] Empty = [];

    public string DefaultRegion { get; set; } = "default";

    public TimeSpan DefaultSingleflightTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public bool EnableDiagnosticsEndpoint { get; set; } = true;

    public IList<string> PolicyAssemblies { get; } = new List<string>();

    public bool PublishInvalidationByDefault { get; set; }
        = true;

    public int DefaultTagCapacity { get; set; } = 256;

    // ── Tiering ───────────────────────────────────────────────────────────────

    /// <summary>Default cache tier policy. Layered = L1 + L2 auto.</summary>
    public CacheTier DefaultTier { get; set; } = CacheTier.Layered;

    /// <summary>Default TTL for cache entries in seconds. 0 = no expiration.</summary>
    public int DefaultTtlSeconds { get; set; } = 300;

    /// <summary>L1-specific default TTL in seconds. Null = derive max(30, L2Ttl/2).</summary>
    public int? DefaultL1TtlSeconds { get; set; }

    /// <summary>Pin Local tier to a specific store by Name. Null = auto-detect via priority.</summary>
    public string? LocalProvider { get; set; }

    /// <summary>Pin Remote tier to a specific store by Name. Null = auto-detect via priority.</summary>
    public string? RemoteProvider { get; set; }

    // ── Coherence ─────────────────────────────────────────────────────────────

    /// <summary>How peer L1 invalidation activates for layered topologies.</summary>
    public CoherenceMode CoherenceMode { get; set; } = CoherenceMode.AutoDetect;

    public IReadOnlyList<string> GetPolicyAssemblies()
        => PolicyAssemblies.Count == 0 ? Empty : new List<string>(PolicyAssemblies);
}
