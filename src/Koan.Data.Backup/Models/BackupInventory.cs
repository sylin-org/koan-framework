namespace Koan.Data.Backup.Models;

/// <summary>
/// Represents the complete backup inventory for an application,
/// including included entities, excluded entities, and coverage warnings.
/// </summary>
/// <remarks>
/// <para>
/// Built during application startup by scanning all <c>Entity&lt;&gt;</c> types
/// and resolving their effective backup policies based on assembly-level
/// <see cref="Attributes.EntityBackupScopeAttribute"/> and entity-level
/// <see cref="Attributes.EntityBackupAttribute"/> declarations.
/// </para>
/// <para>
/// Used for:
/// </para>
/// <list type="bullet">
/// <item><description>Startup validation and warning emission</description></item>
/// <item><description>Backup capability discovery</description></item>
/// <item><description>Health checks and diagnostics</description></item>
/// <item><description>Operator visibility into backup coverage</description></item>
/// </list>
/// </remarks>
public class BackupInventory
{
    /// <summary>
    /// Gets or sets the list of entities included in backup operations.
    /// </summary>
    /// <remarks>
    /// Entities are included via:
    /// <list type="bullet">
    /// <item><description>Assembly-level <c>[EntityBackupScope(Mode = BackupScope.All)]</c></description></item>
    /// <item><description>Entity-level <c>[EntityBackup]</c> attribute</description></item>
    /// </list>
    /// </remarks>
    public List<EntityBackupPolicy> IncludedEntities { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of entities explicitly excluded from backup operations.
    /// </summary>
    /// <remarks>
    /// Entities are excluded via <c>[EntityBackup(Enabled = false)]</c>.
    /// When a <c>Reason</c> is provided, the entity will not generate warnings.
    /// </remarks>
    public List<EntityBackupPolicy> ExcludedEntities { get; set; } = new();

    /// <summary>
    /// Gets or sets the list of warnings about entities without backup coverage.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Warnings are generated for entities that:
    /// </para>
    /// <list type="bullet">
    /// <item><description>Have no assembly-level scope and no entity-level attribute</description></item>
    /// <item><description>Are in a <c>BackupScope.None</c> assembly without <c>[EntityBackup]</c></description></item>
    /// <item><description>Are excluded without a documented <c>Reason</c></description></item>
    /// </list>
    /// <para>
    /// Emitted during startup as <c>[WARN] Koan:backup</c> log entries.
    /// </para>
    /// </remarks>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// Gets or sets the timestamp when this inventory was generated.
    /// </summary>
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the total count of entities with backup coverage (included + excluded).
    /// </summary>
    public int TotalCoveredEntities => IncludedEntities.Count + ExcludedEntities.Count;

    /// <summary>
    /// Gets the total count of entities that will be backed up.
    /// </summary>
    public int TotalIncludedEntities => IncludedEntities.Count;

    /// <summary>
    /// Gets the total count of entities explicitly excluded.
    /// </summary>
    public int TotalExcludedEntities => ExcludedEntities.Count;

    /// <summary>
    /// Gets the total count of warnings.
    /// </summary>
    public int TotalWarnings => Warnings.Count;

    /// <summary>
    /// Gets a value indicating whether this inventory has any warnings.
    /// </summary>
    public bool HasWarnings => Warnings.Count > 0;

    /// <summary>
    /// Gets a value indicating whether this inventory is healthy (no warnings).
    /// </summary>
    public bool IsHealthy => Warnings.Count == 0;
}
