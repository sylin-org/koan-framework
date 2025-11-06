namespace Koan.Data.AI.Attributes;

/// <summary>
/// Configures storage location for EmbedJob entities.
/// By default, jobs are stored in the same partition/source as the entity.
/// Use this attribute to override the storage location for better isolation and monitoring.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public class EmbedStorageAttribute : Attribute
{
    /// <summary>
    /// Partition name for EmbedJob storage (optional).
    /// If null, uses the same partition as the entity.
    /// Example: "embed-jobs" for dedicated job partition.
    /// </summary>
    public string? Partition { get; set; }

    /// <summary>
    /// Source/adapter override for EmbedJob storage (optional).
    /// If null, uses the same source as the entity.
    /// Example: "jobs-db" for separate database.
    /// </summary>
    public string? Source { get; set; }
}
