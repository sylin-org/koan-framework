using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core.Model;
using Sora.Data.Core.Relationships;

namespace S5.Recs.Models;

[DataAdapter("mongo")]
[Storage(Name = "LibraryEntries")]
public sealed class LibraryEntryDoc : Entity<LibraryEntryDoc>
{
    [Parent(typeof(UserDoc))]
    public required string UserId { get; set; }
    [Parent(typeof(AnimeDoc))]
    public required string AnimeId { get; set; }
    public bool Favorite { get; set; }
    public bool Watched { get; set; }
    public bool Dropped { get; set; }
    public int? Rating { get; set; } // 0..5, null = not rated
    public DateTimeOffset AddedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public static string MakeId(string userId, string animeId) => $"{userId}:{animeId}";
}
