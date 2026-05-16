using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Koan.Cache.Abstractions.Coherence;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Options;

public sealed class CacheOptions
{
    private static readonly string[] Empty = [];

    /// <summary>Legacy single-provider field. New deployments use LocalProvider/RemoteProvider.</summary>
    [Required]
    public string Provider { get; set; } = "memory";

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

    /// <summary>How the coherence coordinator activates. Default: AutoDetect (active iff ≥1 channel registered).</summary>
    public CoherenceMode CoherenceMode { get; set; } = CoherenceMode.AutoDetect;

    /// <summary>Pin coherence transport by TransportName. Null = highest [ProviderPriority] wins.</summary>
    public string? CoherenceTransport { get; set; }

    /// <summary>Per-key debounce window for write broadcasts, in milliseconds. 0 = disabled (immediate publish).</summary>
    public int CoherenceCoalescingMs { get; set; } = 0;

    /// <summary>Hard cap on the coalescing buffer. Excess writes flush immediately.</summary>
    public int CoherenceCoalescingMaxBuffered { get; set; } = 10_000;

    /// <summary>Maximum time to wait for a channel Subscribe() at startup, in milliseconds.</summary>
    public int CoherenceStartupTimeoutMs { get; set; } = 10_000;

    public IReadOnlyList<string> GetPolicyAssemblies()
        => PolicyAssemblies.Count == 0 ? Empty : new List<string>(PolicyAssemblies);
}
