using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Optimization;


namespace S5.Recs.Models;

[Storage(Name = "MediaTypes")]
[OptimizeStorage(OptimizationType = StorageOptimizationType.None, Reason = "Uses human-readable string identifiers, not GUIDs")]
public sealed class MediaType : Entity<MediaType>
{
    public required string Name { get; set; }        // "Anime", "Manga"
    public required string DisplayName { get; set; }  // "Anime", "Manga"
    public string? Description { get; set; }
    public int SortOrder { get; set; }

    // ID generation handled automatically by framework
}