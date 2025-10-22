namespace Koan.Data.Backup.Attributes;

/// <summary>
/// Defines backup participation mode for an assembly.
/// </summary>
/// <remarks>
/// <para>
/// Controls the default backup behavior for all entities in an assembly:
/// </para>
/// <list type="bullet">
/// <item><description><see cref="BackupScope.None"/>: Require explicit <see cref="EntityBackupAttribute"/> on each entity (strict mode)</description></item>
/// <item><description><see cref="BackupScope.All"/>: Include all entities by default, allow per-entity overrides</description></item>
/// </list>
/// </remarks>
public enum BackupScope
{
    /// <summary>
    /// Require explicit [EntityBackup] decoration on each entity.
    /// Entities without the attribute will generate startup warnings.
    /// </summary>
    /// <remarks>
    /// Use this for assemblies with sensitive data or when you want strict control
    /// over which entities participate in backups.
    /// </remarks>
    None = 0,

    /// <summary>
    /// Automatically include all Entity&lt;&gt; types in this assembly.
    /// Individual entities can opt-out with [EntityBackup(Enabled = false)].
    /// </summary>
    /// <remarks>
    /// Use this for assemblies where most entities should be backed up by default.
    /// Provides backward compatibility with auto-discovery behavior while allowing
    /// explicit opt-out.
    /// </remarks>
    All = 1
}

/// <summary>
/// Configures assembly-level backup participation and default policies for all entities.
/// </summary>
/// <remarks>
/// <para>
/// Applied at assembly level to set default backup behavior for all <c>Entity&lt;&gt;</c> types
/// in that assembly. Individual entities can override these defaults using <see cref="EntityBackupAttribute"/>.
/// </para>
/// <para>
/// **Usage Examples:**
/// </para>
/// <code>
/// // Opt-in all entities in this assembly
/// [assembly: EntityBackupScope(Mode = BackupScope.All)]
///
/// // Opt-in all with encryption by default
/// [assembly: EntityBackupScope(Mode = BackupScope.All, EncryptByDefault = true)]
///
/// // Require explicit decoration (strict mode)
/// [assembly: EntityBackupScope(Mode = BackupScope.None)]
/// </code>
/// <para>
/// **Policy Resolution:**
/// </para>
/// <list type="number">
/// <item><description>Assembly scope sets the default inclusion/exclusion behavior</description></item>
/// <item><description>Assembly-level <see cref="EncryptByDefault"/> applies to included entities</description></item>
/// <item><description>Entity-level <see cref="EntityBackupAttribute"/> overrides assembly defaults</description></item>
/// </list>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class EntityBackupScopeAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the backup participation mode for entities in this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="BackupScope.None"/>: Only entities with explicit <see cref="EntityBackupAttribute"/> are backed up.
    /// </para>
    /// <para>
    /// <see cref="BackupScope.All"/>: All entities are backed up unless they opt-out with <c>[EntityBackup(Enabled = false)]</c>.
    /// </para>
    /// </remarks>
    public BackupScope Mode { get; set; } = BackupScope.None;

    /// <summary>
    /// Gets or sets whether entities in this assembly should be encrypted by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default is <c>false</c>. When <c>true</c>, all entities included via this scope
    /// will have <c>Encrypt = true</c> unless overridden by entity-level <see cref="EntityBackupAttribute"/>.
    /// </para>
    /// <para>
    /// Use this for assemblies containing primarily sensitive data (e.g., user management, payment processing).
    /// </para>
    /// </remarks>
    public bool EncryptByDefault { get; set; } = false;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityBackupScopeAttribute"/> class.
    /// </summary>
    public EntityBackupScopeAttribute()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityBackupScopeAttribute"/> class
    /// with the specified backup scope mode.
    /// </summary>
    /// <param name="mode">The backup participation mode for this assembly.</param>
    public EntityBackupScopeAttribute(BackupScope mode)
    {
        Mode = mode;
    }
}
