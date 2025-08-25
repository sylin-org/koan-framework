using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core.Model;

namespace S5.Recs.Models;

[DataAdapter("mongo")]
[Storage(Name = "Genres")]
public sealed class GenreStatDoc : Entity<GenreStatDoc>
{
    public required string Genre { get; set; }
    public int AnimeCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
