using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Koan.Cache.Options;

public sealed class CacheOptions
{
    private static readonly string[] Empty = Array.Empty<string>();

    [Required]
    public string Provider { get; set; } = "memory";

    public string DefaultRegion { get; set; } = "default";

    public TimeSpan DefaultSingleflightTimeout { get; set; } = TimeSpan.FromSeconds(5);

    public bool EnableDiagnosticsEndpoint { get; set; } = true;

    public IList<string> PolicyAssemblies { get; } = new List<string>();

    public bool PublishInvalidationByDefault { get; set; }
        = false;

    public int DefaultTagCapacity { get; set; } = 256;

    public IReadOnlyList<string> GetPolicyAssemblies()
        => PolicyAssemblies.Count == 0 ? Empty : new List<string>(PolicyAssemblies);
}
