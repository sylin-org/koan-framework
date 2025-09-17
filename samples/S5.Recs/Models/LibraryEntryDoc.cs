using Koan.Data.Abstractions;
using Koan.Data.Abstractions.Annotations;
using Koan.Data.Core.Model;
using Koan.Data.Core.Optimization;
using Koan.Data.Core.Relationships;
using S5.Recs.Infrastructure;

namespace S5.Recs.Models;

[Storage(Name = "LibraryEntries")]
[OptimizeStorage(OptimizationType = StorageOptimizationType.None, Reason = "Uses composite deterministic string IDs, not GUIDs")]
public sealed class LibraryEntry : Entity<LibraryEntry>
{
    [Parent(typeof(UserDoc))]
    public required string UserId { get; set; }

    [Parent(typeof(Media))]
    public required string MediaId { get; set; }

    public bool Favorite { get; set; }
    public MediaStatus Status { get; set; }  // PlanToConsume, Consuming, Completed, Dropped, OnHold
    public int? Rating { get; set; }         // 1-10 scale
    public int? Progress { get; set; }       // Episodes watched / Chapters read
    public DateTimeOffset AddedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? Notes { get; set; }

    public static string MakeId(string userId, string mediaId) => IdGenerationUtilities.GenerateLibraryEntryId(userId, mediaId);

    // ID generation handled by MakeId method when needed
}
