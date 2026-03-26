using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Koan.Cache.Abstractions.Primitives;

namespace Koan.Cache.Options;

public sealed class CacheOptions
{
    private static readonly string[] Empty = [];

    [Required]
    public string Provider { get; set; } = "memory";

    public string DefaultRegion { get; set; } = "default";

    public TimeSpan DefaultSingleflightTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public bool EnableDiagnosticsEndpoint { get; set; } = true;

    public IList<string> PolicyAssemblies { get; } = new List<string>();

    public bool PublishInvalidationByDefault { get; set; }
        = false;

    public int DefaultTagCapacity { get; set; } = 256;

    /// <summary>Default cache tier policy. Layered = L1 + L2 auto.</summary>
    public CacheTier DefaultTier { get; set; } = CacheTier.Layered;

    /// <summary>Default TTL for cache entries in seconds. 0 = no expiration.</summary>
    public int DefaultTtlSeconds { get; set; } = 300;

    /// <summary>Pin default local provider type. Null = auto-detect highest priority local.</summary>
    public string? LocalProvider { get; set; }

    /// <summary>Pin default remote provider type. Null = auto-detect highest priority remote.</summary>
    public string? RemoteProvider { get; set; }

    public IReadOnlyList<string> GetPolicyAssemblies()
        => PolicyAssemblies.Count == 0 ? Empty : new List<string>(PolicyAssemblies);
}
