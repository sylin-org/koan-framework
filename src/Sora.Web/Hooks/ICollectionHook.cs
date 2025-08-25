namespace Sora.Web.Hooks;

/// <summary>
/// Collection-level lifecycle hooks.
/// </summary>
public interface ICollectionHook<TEntity> : IOrderedHook
{
    Task OnBeforeFetchAsync(HookContext<TEntity> ctx, QueryOptions opts);
    Task OnAfterFetchAsync(HookContext<TEntity> ctx, List<TEntity> items);
}