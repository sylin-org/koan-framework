using System.Collections.Generic;

namespace Koan.AI.Contracts.Sources;

/// <summary>
/// Registry interface for AI sources (collections of members).
/// Implementation handles discovery from configuration and programmatic registration.
///
/// ADR-0015: Source = Collection of members with policy and priority.
/// </summary>
public interface IAiSourceRegistry
{
    /// <summary>
    /// Register a source (for runtime/testing scenarios or auto-discovery)
    /// </summary>
    void RegisterSource(AiSourceDefinition source);

    /// <summary>
    /// Get source definition by name (case-insensitive)
    /// </summary>
    AiSourceDefinition? GetSource(string name);

    /// <summary>
    /// Try to get source definition by name (case-insensitive)
    /// </summary>
    bool TryGetSource(string name, out AiSourceDefinition? source);

    /// <summary>
    /// Get all registered source names
    /// </summary>
    IReadOnlyCollection<string> GetSourceNames();

    /// <summary>
    /// Get all registered sources
    /// </summary>
    IReadOnlyCollection<AiSourceDefinition> GetAllSources();

    /// <summary>
    /// Check if source exists (case-insensitive)
    /// </summary>
    bool HasSource(string name);

    /// <summary>
    /// Get all sources with a specific capability
    /// </summary>
    IReadOnlyCollection<AiSourceDefinition> GetSourcesWithCapability(string capabilityName);
}
