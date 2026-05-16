namespace Koan.Web.Hooks;

/// <summary>
/// Collection-level lifecycle hooks.
/// </summary>
public interface ICollectionHook<TEntity> : IOrderedHook
{
    Task OnBeforeFetch(HookContext<TEntity> ctx, QueryOptions opts);
    Task OnAfterFetch(HookContext<TEntity> ctx, List<TEntity> items);
}