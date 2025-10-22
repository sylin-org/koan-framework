namespace Koan.Data.Backup.Attributes;

/// <summary>
/// Marks an entity type for participation in backup operations.
/// Provides policy configuration for encryption, schema inclusion, and opt-out.
/// </summary>
/// <remarks>
/// <para>
/// This attribute supports explicit opt-in to backup operations, preventing silent data loss
/// from automatic discovery of entities that may not have configured repositories.
/// </para>
/// <para>
/// **Usage Examples:**
/// </para>
/// <code>
/// // Basic opt-in
/// [EntityBackup]
/// public class Media : Entity&lt;Media&gt; { }
///
/// // PII encryption
/// [EntityBackup(Encrypt = true)]
/// public class User : Entity&lt;User&gt; { }
///
/// // Skip schema to reduce size
/// [EntityBackup(IncludeSchema = false)]
/// public class LogEntry : Entity&lt;LogEntry&gt; { }
///
/// // Explicit opt-out with justification
/// [EntityBackup(Enabled = false, Reason = "Derived view, rebuild from source")]
/// public class SearchIndex : Entity&lt;SearchIndex&gt; { }
/// </code>
/// <para>
/// **Policy Resolution:**
/// Entity-level attributes override assembly-level defaults from <see cref="EntityBackupScopeAttribute"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class EntityBackupAttribute : Attribute
{
    /// <summary>
    /// Gets or sets whether this entity should be included in backups.
    /// </summary>
    /// <remarks>
    /// Default is <c>true</c>. Set to <c>false</c> to explicitly exclude an entity
    /// from backup operations. When set to <c>false</c>, provide a <see cref="Reason"/>
    /// to document why the entity is excluded.
    /// </remarks>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets whether backup data for this entity should be encrypted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default is <c>false</c>. When <c>true</c>, backup data will be encrypted
    /// using the configured backup encryption provider.
    /// </para>
    /// <para>
    /// Use this for entities containing PII or sensitive data (e.g., User, Payment).
    /// </para>
    /// <para>
    /// **Note:** Encryption key management must be configured separately.
    /// See Koan.Security integration documentation.
    /// </para>
    /// </remarks>
    public bool Encrypt { get; set; } = false;

    /// <summary>
    /// Gets or sets whether to include schema information in the backup.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Default is <c>true</c>. When <c>false</c>, only entity data is backed up,
    /// reducing backup size and restore time.
    /// </para>
    /// <para>
    /// Set to <c>false</c> for high-volume, low-schema-change entities (e.g., LogEntry).
    /// Restore operations will use the current schema when <c>IncludeSchema = false</c>.
    /// </para>
    /// </remarks>
    public bool IncludeSchema { get; set; } = true;

    /// <summary>
    /// Gets or sets the reason for excluding this entity from backups.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Required when <see cref="Enabled"/> is <c>false</c>. Provides documentation
    /// for operators and auditors about why an entity is intentionally excluded.
    /// </para>
    /// <para>
    /// Common reasons:
    /// - "Derived view, rebuild from source"
    /// - "Cache data, no backup needed"
    /// - "Test entity, excluded in production"
    /// </para>
    /// <para>
    /// If <c>Reason</c> is provided, startup inventory will not emit warnings about
    /// missing backup coverage.
    /// </para>
    /// </remarks>
    public string? Reason { get; set; }
}
