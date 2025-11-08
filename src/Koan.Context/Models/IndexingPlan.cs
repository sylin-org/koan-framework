using Koan.Context.Services;

namespace Koan.Context.Models;

/// <summary>
/// Result of differential scan planning - categorizes files by change type
/// </summary>
public class IndexingPlan
{
    /// <summary>
    /// Files that are new (not in manifest)
    /// </summary>
    public List<DiscoveredFile> NewFiles { get; set; } = new();

    /// <summary>
    /// Files with content changes (hash mismatch)
    /// </summary>
    public List<DiscoveredFile> ChangedFiles { get; set; } = new();

    /// <summary>
    /// Files completely unchanged (can skip)
    /// </summary>
    public List<DiscoveredFile> SkippedFiles { get; set; } = new();

    /// <summary>
    /// Files that exist in manifest but not on disk (deleted)
    /// </summary>
    public List<string> DeletedFiles { get; set; } = new();

    /// <summary>
    /// Total files to process (new + changed)
    /// </summary>
    public int TotalFilesToProcess => NewFiles.Count + ChangedFiles.Count;

    /// <summary>
    /// Total files discovered
    /// </summary>
    public int TotalFiles => NewFiles.Count + ChangedFiles.Count + SkippedFiles.Count;

    /// <summary>
    /// Estimated time savings from skipping unchanged files
    /// </summary>
    public TimeSpan EstimatedTimeSavings { get; set; }

    /// <summary>
    /// Time taken to compute the plan
    /// </summary>
    public TimeSpan PlanningTime { get; set; }

    /// <summary>
    /// Summary statistics for logging
    /// </summary>
    public override string ToString()
    {
        return $"Plan: {NewFiles.Count} new, {ChangedFiles.Count} changed, " +
               $"{SkippedFiles.Count} skipped, {DeletedFiles.Count} deleted | " +
               $"Processing {TotalFilesToProcess}/{TotalFiles} files";
    }
}

/// <summary>
/// Extended file information for differential indexing
/// </summary>
public record FileManifestEntry
{
    public string RelativePath { get; init; } = string.Empty;
    public string FileHash { get; init; } = string.Empty;
    public DateTime LastModified { get; init; }
    public long SizeBytes { get; init; }
}
