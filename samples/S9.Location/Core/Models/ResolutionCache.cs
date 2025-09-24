using System;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace S9.Location.Core.Models;

[Storage(Name = "location_resolution_cache", Namespace = "s9")]
public class ResolutionCache : Entity<ResolutionCache>
{
    public string CanonicalLocationId { get; set; } = string.Empty;
    public string NormalizedAddress { get; set; } = string.Empty;
    public double Confidence { get; set; }
    public DateTimeOffset ResolvedAt { get; set; } = DateTimeOffset.UtcNow;

    public static ResolutionCache Create(string addressHash, string normalized, string canonicalId, double confidence) => new()
    {
        Id = addressHash,
        NormalizedAddress = normalized,
        CanonicalLocationId = canonicalId,
        Confidence = confidence,
        ResolvedAt = DateTimeOffset.UtcNow
    };
}
