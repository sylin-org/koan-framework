namespace Koan.Web.Hooks;

/// <summary>
/// Model-level lifecycle hooks.
/// </summary>
public interface IModelHook<TEntity> : IOrderedHook
{
    Task OnBeforeFetch(HookContext<TEntity> ctx, string id);
    Task OnAfterFetch(HookContext<TEntity> ctx, TEntity? model);
    Task OnBeforeSave(HookContext<TEntity> ctx, TEntity model);
    Task OnAfterSave(HookContext<TEntity> ctx, TEntity model);
    Task OnBeforeDelete(HookContext<TEntity> ctx, TEntity model);
    Task OnAfterDelete(HookContext<TEntity> ctx, TEntity model);
    Task OnBeforePatch(HookContext<TEntity> ctx, string id, object patch);
    Task OnAfterPatch(HookContext<TEntity> ctx, TEntity model);
}