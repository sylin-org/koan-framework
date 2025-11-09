using System;

namespace Koan.Data.Abstractions.Naming;

/// <summary>
/// Shared composition logic for storage/collection naming across data and vector layers.
/// Implements the canonical pattern: [StorageName] or [StorageName][Separator][Partition]
/// Used by both StorageNameRegistry (data) and VectorStorageNameRegistry (vector).
/// </summary>
public static class NamingComposer
{
    /// <summary>
    /// Compose final storage name with optional partition suffix.
    /// Applies trimming at all stages to handle whitespace gracefully.
    /// </summary>
    /// <param name="provider">Naming provider (data or vector adapter factory)</param>
    /// <param name="entityType">Entity type to generate name for</param>
    /// <param name="partition">Optional partition identifier (e.g., "proj-abc123")</param>
    /// <param name="services">Service provider for dependency resolution</param>
    /// <returns>Fully composed storage name, optionally with partition suffix</returns>
    /// <example>
    /// Without partition: "Todos"
    /// With partition: "Todos#proj-abc123" (data) or "Todos_proj_abc123" (vector)
    /// </example>
    public static string Compose(
        INamingProvider provider,
        Type entityType,
        string? partition,
        IServiceProvider services)
    {
        // Get and trim base storage name
        var storageName = provider.GetStorageName(entityType, services).Trim();

        // Trim and check partition
        var trimmedPartition = partition?.Trim();
        if (string.IsNullOrEmpty(trimmedPartition))
            return storageName;

        // Compose with partition
        var concretePartition = provider.GetConcretePartition(trimmedPartition).Trim();
        return storageName + provider.RepositorySeparator + concretePartition;
    }
}
