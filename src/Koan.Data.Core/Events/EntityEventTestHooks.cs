namespace Koan.Data.Core.Events;

/// <summary>
/// Test-facing helpers for resetting entity event pipelines between scenarios.
/// </summary>
public static class EntityEventTestHooks
{
    public static void Reset<TEntity, TKey>()
        where TEntity : class
        where TKey : notnull
        => EntityEventRegistry<TEntity, TKey>.Reset();
}
