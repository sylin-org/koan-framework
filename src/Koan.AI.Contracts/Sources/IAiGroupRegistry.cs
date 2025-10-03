using System.Collections.Generic;

namespace Koan.AI.Contracts.Sources;

/// <summary>
/// DEPRECATED: Use IAiSourceRegistry instead.
/// ADR-0015 corrected terminology: "Group" was actually "Source" (collection of members).
/// This interface is obsolete and will be removed.
/// </summary>
[System.Obsolete("Use IAiSourceRegistry - terminology corrected per ADR-0015")]
public interface IAiGroupRegistry
{
    /// <summary>
    /// Register a group (for runtime/testing scenarios or auto-discovery)
    /// </summary>
    void RegisterGroup(AiGroupDefinition group);

    /// <summary>
    /// Get group definition by name (case-insensitive)
    /// </summary>
    AiGroupDefinition? GetGroup(string name);

    /// <summary>
    /// Try to get group definition by name (case-insensitive)
    /// </summary>
    bool TryGetGroup(string name, out AiGroupDefinition? group);

    /// <summary>
    /// Get all registered group names
    /// </summary>
    IReadOnlyCollection<string> GetGroupNames();

    /// <summary>
    /// Get all registered groups
    /// </summary>
    IReadOnlyCollection<AiGroupDefinition> GetAllGroups();

    /// <summary>
    /// Check if group exists (case-insensitive)
    /// </summary>
    bool HasGroup(string name);

    /// <summary>
    /// Ensure a group exists, creating a default one if not
    /// </summary>
    AiGroupDefinition EnsureGroup(string name);
}
