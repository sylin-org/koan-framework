namespace Koan.Web.Hooks;

/// <summary>
/// Hook invoked while building query options from the request.
/// </summary>
public interface IRequestOptionsHook<TEntity> : IOrderedHook
{
    Task OnBuildingOptions(HookContext<TEntity> ctx, QueryOptions opts);
}