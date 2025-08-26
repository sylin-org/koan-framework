using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core.Model;

namespace S5.Recs.Models;

// User profile for personalization (genre weights + preference vector)
[DataAdapter("mongo")]
[Storage(Name = "UserProfiles")]
public sealed class UserProfileDoc : Entity<UserProfileDoc>
{
    public Dictionary<string, double> GenreWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public float[]? PrefVector { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
