using Sora.Data.Core.Model;
using Sora.Data.Abstractions.Annotations;

namespace S8.Location.Core.Models;

[Storage(Name = "ResolutionCache")]
public class ResolutionCache : Entity<ResolutionCache, string>
{
    public string CanonicalUlid { get; set; } = ""; // AgnosticLocation.Id
    public string NormalizedAddress { get; set; } = "";
    public DateTime ResolvedAt { get; set; } = DateTime.UtcNow;
}