using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;

namespace S5.Recs.Models;

// User profile for personalization (genre weights + preference vector)
[DataAdapter("mongo")]
[Storage(Name = "UserProfiles")]
public sealed class UserProfileDoc : IEntity<string>
{
    public string Id { get; set; } = string.Empty; // userId
    public Dictionary<string,double> GenreWeights { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public float[]? PrefVector { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
