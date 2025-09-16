using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Relationships;


namespace S5.Recs.Models;

[DataAdapter("mongo")]
[Storage(Name = "MediaFormats")]
public sealed class MediaFormat : Entity<MediaFormat>
{
    [Parent(typeof(MediaType))]
    public required string MediaTypeId { get; set; }

    public required string Name { get; set; }        // "TV", "Movie", "OVA", "Manga", "Light Novel"
    public required string DisplayName { get; set; }  // "TV Series", "Movie", "OVA", "Manga", "Light Novel"
    public string? Description { get; set; }
    public int SortOrder { get; set; }

    // ID generation handled automatically by framework
}