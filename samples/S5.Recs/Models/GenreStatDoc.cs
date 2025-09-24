using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace S5.Recs.Models;

[Storage(Name = "Genres")]
public sealed class GenreStatDoc : Entity<GenreStatDoc>
{
    public required string Genre { get; set; }
    public int MediaCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
