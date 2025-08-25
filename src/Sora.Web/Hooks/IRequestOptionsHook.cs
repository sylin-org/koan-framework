namespace Sora.Web.Hooks;

/// <summary>
/// Hook invoked while building query options from the request.
/// </summary>
public interface IRequestOptionsHook<TEntity> : IOrderedHook
{
    Task OnBuildingOptionsAsync(HookContext<TEntity> ctx, QueryOptions opts);
}