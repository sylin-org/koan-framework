namespace Koan.Web.Hooks;

/// <summary>
/// Model-level lifecycle hooks.
/// </summary>
public interface IModelHook<TEntity> : IOrderedHook
{
    Task OnBeforeFetchAsync(HookContext<TEntity> ctx, string id);
    Task OnAfterFetchAsync(HookContext<TEntity> ctx, TEntity? model);
    Task OnBeforeSaveAsync(HookContext<TEntity> ctx, TEntity model);
    Task OnAfterSaveAsync(HookContext<TEntity> ctx, TEntity model);
    Task OnBeforeDeleteAsync(HookContext<TEntity> ctx, TEntity model);
    Task OnAfterDeleteAsync(HookContext<TEntity> ctx, TEntity model);
    Task OnBeforePatchAsync(HookContext<TEntity> ctx, string id, object patch);
    Task OnAfterPatchAsync(HookContext<TEntity> ctx, TEntity model);
}