using Koan.Data.Core;
using Koan.Data.Core.Model;

namespace S6.SnapVault.Models;

/// <summary>
/// Represents a photography event (wedding, conference, birthday, etc.)
/// </summary>
public class Event : Entity<Event>
{
    public string Name { get; set; } = "";
    public EventType Type { get; set; }
    public DateTime EventDate { get; set; }
    public string? ClientName { get; set; }
    public string? Location { get; set; }
    public string? Description { get; set; }
    public string? GalleryPassword { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;

    // Computed properties
    public int PhotoCount { get; set; }
    public StorageTier CurrentTier { get; set; } = StorageTier.Hot;
    public long TotalStorageBytes { get; set; }
    public ProcessingStatus ProcessingStatus { get; set; } = ProcessingStatus.Pending;

    // Storage tier breakdown
    public long HotStorageBytes { get; set; }
    public long WarmStorageBytes { get; set; }
    public long ColdStorageBytes { get; set; }
}

public enum EventType
{
    Wedding,
    Corporate,
    Birthday,
    Graduation,
    Anniversary,
    DailyAuto,  // Auto-generated daily albums based on capture date
    Other
}

public enum StorageTier
{
    Hot,    // Recent events, frequent access, CDN-backed
    Warm,   // Older events, occasional access
    Cold    // Archived events, rare access, cost-optimized
}

public enum ProcessingStatus
{
    Pending,
    InProgress,
    Completed,
    Failed,
    PartialSuccess
}
