namespace Koan.ZenGarden;

/// <summary>
/// Configuration options for Zen Garden client.
/// </summary>
public sealed class ZenGardenOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "ZenGarden";
    
    /// <summary>Enable or disable Zen Garden discovery.</summary>
    public bool Enabled { get; set; } = true;
    
    /// <summary>UDP discovery timeout in seconds.</summary>
    public int DiscoveryTimeoutSeconds { get; set; } = Constants.Discovery.DefaultTimeoutSeconds;
    
    /// <summary>HTTP request timeout in seconds.</summary>
    public int HttpTimeoutSeconds { get; set; } = 10;
    
    /// <summary>Enable Chirp listener for hot-cache topology.</summary>
    public bool EnableChirpListener { get; set; } = false;
    
    /// <summary>Multicast group override (defaults to 239.255.42.99).</summary>
    public string? MulticastGroup { get; set; }
    
    /// <summary>Discovery port override (defaults to 7184).</summary>
    public int? DiscoveryPort { get; set; }
    
    /// <summary>
    /// Custom scheme mappings (offering → scheme).
    /// Merged with built-in mappings.
    /// </summary>
    public Dictionary<string, string>? SchemeMappings { get; set; }
}
