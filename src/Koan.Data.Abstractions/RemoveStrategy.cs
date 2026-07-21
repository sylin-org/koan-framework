namespace Koan.Data.Abstractions;

/// <summary>
/// Strategy for bulk removal operations.
/// </summary>
public enum RemoveStrategy
{
    /// <summary>
    /// Safe removal with per-entity Lifecycle participation and provider transaction support.
    /// <para>
    /// - Runs configured BeforeRemove/AfterRemove handlers<br/>
    /// - Participates in transactions<br/>
    /// - Returns exact count of deleted records<br/>
    /// - Safe for production use
    /// </para>
    /// </summary>
    Safe = 0,

    /// <summary>
    /// Fast removal explicitly bypassing per-entity Lifecycle for maximum performance.
    /// <para>
    /// - BYPASSES BeforeRemove/AfterRemove handlers<br/>
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
    /// Framework chooses an optimal strategy while preserving configured Lifecycle semantics.
    /// <para>
    /// - Lifecycle configured: Uses Safe so every visible entity participates<br/>
    /// - No Lifecycle: Provider may use Fast (TRUNCATE, DROP, etc.) when supported<br/>
    /// - Provider lacks FastRemove: Uses Safe<br/>
    /// - Default choice when strategy not explicitly specified<br/>
    /// - Consistent behavior across all environments
    /// </para>
    /// </summary>
    Optimized = 2
}
