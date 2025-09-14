using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core.Model;
using Sora.Data.Core.Relationships;

namespace S5.Recs.Models;

// User profile for personalization (genre weights + preference vector)
[DataAdapter("mongo")]
[Storage(Name = "UserProfiles")]
public sealed class UserProfileDoc : Entity<UserProfileDoc>
{
    [Parent(typeof(UserDoc))]
    public required string UserId { get; set; }
    public Dictionary<string, double> GenreWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public float[]? PrefVector { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
