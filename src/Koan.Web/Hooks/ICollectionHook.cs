namespace Koan.Web.Hooks;

/// <summary>
/// Collection-level lifecycle hooks. Each step does nothing unless a hook says otherwise.
/// </summary>
public interface ICollectionHook<TEntity> : IOrderedHook
{
    Task OnBeforeFetch(HookContext<TEntity> ctx, QueryOptions opts) => Task.CompletedTask;
    Task OnAfterFetch(HookContext<TEntity> ctx, List<TEntity> items) => Task.CompletedTask;
}
