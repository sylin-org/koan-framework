using Koan.Data.Abstractions;
using Koan.Web.Hooks;

namespace Koan.Web.Endpoints;

/// <summary>
/// Abstraction for orchestrating entity hooks outside of HTTP controllers.
/// Allows alternative protocols to plug in custom sequencing or diagnostics.
/// </summary>
public interface IEntityHookPipeline<TEntity>
{
    HookContext<TEntity> CreateContext(EntityRequestContext requestContext);

    Task<bool> BuildOptions(HookContext<TEntity> context, QueryOptions options);

    Task<bool> BeforeCollection(HookContext<TEntity> context, QueryOptions options);

    Task<bool> AfterCollection(HookContext<TEntity> context, List<TEntity> items);

    Task<bool> BeforeModelFetch(HookContext<TEntity> context, string id);

    Task<bool> AfterModelFetch(HookContext<TEntity> context, TEntity? model);

    Task<bool> BeforeSave(HookContext<TEntity> context, TEntity model);

    Task<bool> AfterSave(HookContext<TEntity> context, TEntity model);

    Task<bool> BeforeDelete(HookContext<TEntity> context, TEntity model);

    Task<bool> AfterDelete(HookContext<TEntity> context, TEntity model);

    Task<bool> BeforePatch(HookContext<TEntity> context, string id, object patch);

    Task<bool> AfterPatch(HookContext<TEntity> context, TEntity model);

    Task<(bool replaced, object payload)> EmitCollection(HookContext<TEntity> context, object payload);

    Task<(bool replaced, object payload)> EmitModel(HookContext<TEntity> context, object payload);
}
