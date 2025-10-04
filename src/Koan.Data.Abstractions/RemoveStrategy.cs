namespace Koan.Data.Abstractions;

/// <summary>
/// Strategy for bulk removal operations.
/// </summary>
public enum RemoveStrategy
{
    /// <summary>
    /// Safe removal with lifecycle hooks and full transaction support.
    /// <para>
    /// - Fires BeforeDelete/AfterDelete hooks<br/>
    /// - Participates in transactions<br/>
    /// - Returns exact count of deleted records<br/>
    /// - Safe for production use
    /// </para>
    /// </summary>
    Safe = 0,

    /// <summary>
    /// Fast removal bypassing lifecycle hooks for maximum performance.
    /// <para>
    /// - BYPASSES BeforeDelete/AfterDelete hooks<br/>
    /// - May not participate in transactions (provider-dependent)<br/>
    /// - Resets auto-increment/identity counters<br/>
    /// - 10-100x faster on large tables<br/>
    /// - Use for test cleanup, staging resets, non-production scenarios
    /// </para>
    /// <para>
    /// Provider implementations:<br/>
    /// - PostgreSQL/SQL Server: TRUNCATE TABLE<br/>
    /// - MongoDB: Drop collection + recreate<br/>
    /// - SQLite: DELETE + VACUUM<br/>
    /// - Redis: UNLINK (async deletion)<br/>
    /// - JSON/InMemory: No fast path available
    /// </para>
    /// </summary>
    Fast = 1,

    /// <summary>
    /// Framework chooses optimal strategy based on provider capabilities.
    /// <para>
    /// - Provider supports FastRemove: Uses Fast (TRUNCATE, DROP, etc.)<br/>
    /// - Provider lacks FastRemove: Uses Safe (DELETE with hooks)<br/>
    /// - Default choice when strategy not explicitly specified<br/>
    /// - Consistent behavior across all environments
    /// </para>
    /// </summary>
    Optimized = 2
}
