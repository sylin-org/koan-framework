using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace S5.Recs.Models;

[DataAdapter("mongo")]
[Storage(Name = "Tags")]
public sealed class TagStatDoc : Entity<TagStatDoc>
{
    public required string Tag { get; set; }
    public int MediaCount { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
