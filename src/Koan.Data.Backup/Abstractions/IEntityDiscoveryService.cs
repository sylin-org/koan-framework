using Koan.Data.Backup.Models;

namespace Koan.Data.Backup.Abstractions;

public interface IEntityDiscoveryService
{
    /// <summary>
    /// Discovers all <see cref="Koan.Data.Abstractions.IEntity{TKey}"/> implementations in the application domain.
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

    /// <summary>
    /// Builds the backup inventory by scanning all Entity&lt;&gt; types and resolving their
    /// backup policies based on assembly-level and entity-level attributes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resolves effective backup policies using:
    /// </para>
    /// <list type="number">
    /// <item><description>Assembly-level <c>[EntityBackupScope]</c> attributes</description></item>
    /// <item><description>Entity-level <c>[EntityBackup]</c> attributes</description></item>
    /// <item><description>Policy resolution rules (see <see cref="EntityBackupPolicy"/>)</description></item>
    /// </list>
    /// <para>
    /// Generates warnings for entities without backup coverage in strict mode assemblies.
    /// </para>
    /// </remarks>
    /// <param name="ct">Cancellation token</param>
    /// <returns>A <see cref="BackupInventory"/> containing included, excluded, and uncovered entities.</returns>
    Task<BackupInventory> BuildInventoryAsync(CancellationToken ct = default);
}