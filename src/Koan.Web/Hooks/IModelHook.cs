namespace Koan.Web.Hooks;

/// <summary>
/// Model-level lifecycle hooks. Each step does nothing unless a hook says otherwise, so a hook implements only the steps it needs.
/// </summary>
public interface IModelHook<TEntity> : IOrderedHook
{
    Task OnBeforeFetch(HookContext<TEntity> ctx, string id) => Task.CompletedTask;
    Task OnAfterFetch(HookContext<TEntity> ctx, TEntity? model) => Task.CompletedTask;
    Task OnBeforeSave(HookContext<TEntity> ctx, TEntity model) => Task.CompletedTask;
    Task OnAfterSave(HookContext<TEntity> ctx, TEntity model) => Task.CompletedTask;
    Task OnBeforeDelete(HookContext<TEntity> ctx, TEntity model) => Task.CompletedTask;
    Task OnAfterDelete(HookContext<TEntity> ctx, TEntity model) => Task.CompletedTask;
    Task OnBeforePatch(HookContext<TEntity> ctx, string id, object patch) => Task.CompletedTask;
    Task OnAfterPatch(HookContext<TEntity> ctx, TEntity model) => Task.CompletedTask;
}
