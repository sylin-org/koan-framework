using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;

namespace S5.Recs.Models;

// User profile for personalization (genre weights + preference vector)

[Storage(Name = "UserProfiles")]
public sealed class UserProfileDoc : Entity<UserProfileDoc>
{
    // UserId is a simple string identifier, not a foreign key relationship
    public required string UserId { get; set; }
    public Dictionary<string, double> GenreWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public float[]? PrefVector { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
