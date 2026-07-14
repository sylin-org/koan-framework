namespace Koan.Data.Core.Events;

/// <summary>
/// Test-facing helpers for inspecting and resetting entity event pipelines between scenarios.
/// </summary>
public static class EntityEventTestHooks
{
    /// <summary>
    /// Gets the number of handlers currently registered for the after-upsert stage.
    /// </summary>
    public static int GetAfterUpsertHandlerCount<TEntity, TKey>()
        where TEntity : class
        where TKey : notnull
        => EntityEventRegistry<TEntity, TKey>.AfterUpsertHandlers.Length;

    public static void Reset<TEntity, TKey>()
        where TEntity : class
        where TKey : notnull
        => EntityEventRegistry<TEntity, TKey>.Reset();
}
