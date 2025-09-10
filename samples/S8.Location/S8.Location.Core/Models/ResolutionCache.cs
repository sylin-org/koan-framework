using Sora.Data.Core.Model;
using Sora.Data.Abstractions.Annotations;
using Sora.Core.Utilities.Ids;

namespace S8.Location.Core.Models;

/// <summary>
/// Cache for address resolution to prevent duplicate processing.
/// Uses SHA512 hash as the ID for efficient duplicate detection.
/// Entity&lt;&gt; already implements string Id property, so no need for explicit generic parameter.
/// </summary>
[Storage(Name = "resolution_cache", Namespace = "s8")]
public class ResolutionCache : Entity<ResolutionCache>
{
    /// <summary>Reference to the canonical AgnosticLocation ID (ULID)</summary>
    public string CanonicalUlid { get; set; } = "";
    
    /// <summary>The normalized address text that was resolved</summary>
    public string NormalizedAddress { get; set; } = "";
    
    /// <summary>When this address was first resolved</summary>
    public DateTime ResolvedAt { get; set; } = DateTime.UtcNow;
    
    /// <summary>
    /// Create a new resolution cache entry with ULID for the canonical location
    /// </summary>
    /// <param name="addressHash">SHA512 hash of the normalized address (becomes the Id)</param>
    /// <param name="normalizedAddress">The normalized address text</param>
    /// <param name="canonicalUlid">ULID of the AgnosticLocation this resolves to</param>
    /// <returns>New ResolutionCache instance</returns>
    public static ResolutionCache Create(string addressHash, string normalizedAddress, string canonicalUlid)
    {
        return new ResolutionCache
        {
            Id = addressHash, // SHA512 hash as primary key
            NormalizedAddress = normalizedAddress,
            CanonicalUlid = canonicalUlid,
            ResolvedAt = DateTime.UtcNow
        };
    }
}