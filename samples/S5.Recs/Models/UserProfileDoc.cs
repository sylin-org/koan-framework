using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace S5.Recs.Models;

// User profile for personalization (genre weights + preference vector)
[DataAdapter("couchbase")]
[Storage(Name = "UserProfiles")]
public sealed class UserProfileDoc : Entity<UserProfileDoc>
{
    [Parent(typeof(UserDoc))]
    public required string UserId { get; set; }
    public Dictionary<string, double> GenreWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public float[]? PrefVector { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
