using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;


namespace S5.Recs.Models;

[DataAdapter("mongo")]
[Storage(Name = "MediaTypes")]
public sealed class MediaType : Entity<MediaType>
{
    public required string Name { get; set; }        // "Anime", "Manga"
    public required string DisplayName { get; set; }  // "Anime", "Manga"
    public string? Description { get; set; }
    public int SortOrder { get; set; }

    // ID generation handled automatically by framework
}