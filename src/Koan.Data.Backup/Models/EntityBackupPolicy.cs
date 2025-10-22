namespace Koan.Data.Backup.Models;

/// <summary>
/// Represents the resolved backup policy for a single entity type.
/// </summary>
/// <remarks>
/// <para>
/// Computed during startup by merging assembly-level defaults from
/// <see cref="Attributes.EntityBackupScopeAttribute"/> with entity-level overrides
/// from <see cref="Attributes.EntityBackupAttribute"/>.
/// </para>
/// <para>
/// Policy resolution follows these rules:
/// </para>
/// <list type="number">
/// <item><description>No assembly scope + No entity attribute → Warning (not backed up)</description></item>
/// <item><description><c>BackupScope.All</c> + No entity attribute → Included (inherit assembly defaults)</description></item>
/// <item><description><c>BackupScope.All</c> + <c>[EntityBackup]</c> → Included (entity overrides assembly)</description></item>
/// <item><description><c>BackupScope.None</c> + No entity attribute → Warning (not backed up)</description></item>
/// <item><description><c>BackupScope.None</c> + <c>[EntityBackup]</c> → Included (explicit opt-in)</description></item>
/// <item><description>Any scope + <c>[EntityBackup(Enabled = false)]</c> → Excluded (explicit opt-out, no warning if Reason provided)</description></item>
/// </list>
/// </remarks>
public class EntityBackupPolicy
{
    /// <summary>
    /// Gets or sets the entity type this policy applies to.
    /// </summary>
    public Type EntityType { get; set; } = default!;

    /// <summary>
    /// Gets or sets the simple name of the entity type.
    /// </summary>
    /// <remarks>
    /// Cached from <see cref="EntityType"/>.Name for convenience in logging and diagnostics.
    /// </remarks>
    public string EntityName => EntityType?.Name ?? "<unknown>";

    /// <summary>
    /// Gets or sets the full name of the entity type.
    /// </summary>
    /// <remarks>
    /// Cached from <see cref="EntityType"/>.FullName for use in manifests and APIs.
    /// </remarks>
    public string EntityFullName => EntityType?.FullName ?? "<unknown>";

    /// <summary>
    /// Gets or sets whether backup data for this entity should be encrypted.
    /// </summary>
    /// <remarks>
    /// Resolved from:
    /// <list type="number">
    /// <item><description>Entity-level <c>[EntityBackup(Encrypt = ...)]</c></description></item>
    /// <item><description>Assembly-level <c>[EntityBackupScope(EncryptByDefault = ...)]</c></description></item>
    /// <item><description>Default: <c>false</c></description></item>
    /// </list>
    /// </remarks>
    public bool Encrypt { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to include schema information in the backup for this entity.
    /// </summary>
    /// <remarks>
    /// Resolved from:
    /// <list type="number">
    /// <item><description>Entity-level <c>[EntityBackup(IncludeSchema = ...)]</c></description></item>
    /// <item><description>Default: <c>true</c></description></item>
    /// </list>
    /// </remarks>
    public bool IncludeSchema { get; set; } = true;

    /// <summary>
    /// Gets or sets the source of this policy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Possible values:
    /// </para>
    /// <list type="bullet">
    /// <item><description>"Assembly" - Policy inherited from <c>[assembly: EntityBackupScope]</c></description></item>
    /// <item><description>"Attribute" - Policy from entity-level <c>[EntityBackup]</c></description></item>
    /// <item><description>"Default" - No explicit policy (fallback behavior)</description></item>
    /// </list>
    /// <para>
    /// Used for diagnostics and boot report output to show operators where policies originate.
    /// </para>
    /// </remarks>
    public string Source { get; set; } = "Default";

    /// <summary>
    /// Gets or sets the reason for excluding this entity from backups.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only populated when <c>[EntityBackup(Enabled = false, Reason = "...")]</c> is used.
    /// </para>
    /// <para>
    /// Provides documentation for operators about why an entity is intentionally excluded.
    /// When a reason is provided, startup inventory will not emit warnings for this exclusion.
    /// </para>
    /// </remarks>
    public string? Reason { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this entity is included in backups.
    /// </summary>
    /// <remarks>
    /// Computed during policy resolution. <c>false</c> when <c>[EntityBackup(Enabled = false)]</c> is used.
    /// </remarks>
    public bool IsIncluded { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether this entity is explicitly excluded.
    /// </summary>
    /// <remarks>
    /// Computed during policy resolution. <c>true</c> when <c>[EntityBackup(Enabled = false)]</c> is used.
    /// </remarks>
    public bool IsExcluded => !IsIncluded;

    /// <summary>
    /// Gets a value indicating whether this exclusion should generate a warning.
    /// </summary>
    /// <remarks>
    /// <c>false</c> when a <see cref="Reason"/> is provided, <c>true</c> otherwise.
    /// </remarks>
    public bool ShouldWarnIfExcluded => IsExcluded && string.IsNullOrWhiteSpace(Reason);
}
