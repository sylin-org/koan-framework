using System;

namespace Koan.Data.Core.Optimization;

/// <summary>
/// Storage optimization types for entity identifiers.
/// </summary>
public enum StorageOptimizationType
{
    /// <summary>No optimization applied.</summary>
    None,
    /// <summary>Optimize string IDs that contain valid GUIDs.</summary>
    Guid
    // Future: Int32, Int64, Binary, etc.
}

/// <summary>
/// Marks an entity for storage optimization. Only applies to string-keyed entities.
/// The framework will validate that string IDs are compatible with the specified optimization type.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class OptimizeStorageAttribute : Attribute
{
    /// <summary>
    /// Type of storage optimization to apply.
    /// </summary>
    public StorageOptimizationType OptimizationType { get; set; } = StorageOptimizationType.Guid;

    /// <summary>
    /// Optional reason for the optimization (for documentation/debugging).
    /// </summary>
    public string Reason { get; set; } = "Entity marked for storage optimization";

    public OptimizeStorageAttribute() { }

    public OptimizeStorageAttribute(StorageOptimizationType optimizationType, string reason = "")
    {
        OptimizationType = optimizationType;
        if (!string.IsNullOrEmpty(reason))
            Reason = reason;
    }
}

/// <summary>
/// Storage optimization metadata for an entity type.
/// Cached in AggregateBag for efficient access.
/// </summary>
public sealed class StorageOptimizationInfo
{
    public StorageOptimizationType OptimizationType { get; init; } = StorageOptimizationType.None;
    public string IdPropertyName { get; init; } = "Id";
    public string Reason { get; init; } = "";

    public bool IsOptimized => OptimizationType != StorageOptimizationType.None;

    public static readonly StorageOptimizationInfo None = new();
}

