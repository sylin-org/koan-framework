using Sora.Data.Abstractions;
using Sora.Data.Abstractions.Annotations;
using Sora.Data.Core.Model;

namespace S5.Recs.Models;

[DataAdapter("mongo")]
[Storage(Name = "RecsCensorTags")]
public sealed class CensorTagsDoc : Entity<CensorTagsDoc>
{
    public List<string> Tags { get; set; } = new();
    public DateTimeOffset UpdatedAt { get; set; }
}
