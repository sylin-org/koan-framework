using Koan.Data.Backup.Models;

namespace Koan.Data.Backup.Abstractions;

public interface IEntityDiscoveryService
{
    /// <summary>
    /// Discovers all Entity<> types in the application domain
    /// </summary>
    Task<EntityDiscoveryResult> DiscoverAllEntitiesAsync(CancellationToken ct = default);

    /// <summary>
    /// Warms up all discovered entities by activating their AggregateConfigs
    /// </summary>
    Task WarmupAllEntitiesAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the currently discovered entities (cached)
    /// </summary>
    IEnumerable<EntityTypeInfo> GetDiscoveredEntities();

    /// <summary>
    /// Clears the discovery cache and forces re-discovery
    /// </summary>
    Task RefreshDiscoveryAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets discovery statistics
    /// </summary>
    EntityDiscoveryResult GetDiscoveryStats();
}