using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core.Model;

namespace S5.Recs.Models;

[DataAdapter("mongo")]
[Storage(Name = "Tags")]
public sealed class TagStatDoc : Entity<TagStatDoc>
{
    public required string Tag { get; set; }
    public int AnimeCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
