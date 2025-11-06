using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;

namespace S5.Recs.Models;


[Storage(Name = "Tags")]
public sealed class TagStatDoc : Entity<TagStatDoc>
{
    public required string Tag { get; set; }
    public int MediaCount { get; set; }

    /// <summary>
    /// Whether this tag is marked as NSFW by preemptive baseline filter.
    /// Added in ARCH-0069: Partition-Based Import Pipeline Architecture.
    /// </summary>
    public bool IsNsfw { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
